using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Trade;
using AgroTrade.Infrastructure.Auth;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// Builds and sends the event-driven emails (shipment booked, quotation received, payment
/// lifecycle, verification and dispute alerts) using the same branded templates as the auth flow.
/// </summary>
/// <remarks>
/// <para>
/// Every method is guarded: the order, payment or verification decision has already been committed
/// by the time these run, so a failed lookup or SMTP dial must not turn a successful action into a
/// 500. Failures are logged with the recipient masked, and each message is independent — one bad
/// address does not stop the next administrator being told.
/// </para>
/// <para>
/// The recipients are resolved from the database rather than passed in by the caller, so the
/// business services stay free of email concerns and cannot accidentally email an unverified or
/// suspended account.
/// </para>
/// </remarks>
public sealed class TransactionalEmailService(
    AgroTradeDbContext db,
    IEmailSender emailSender,
    IOptions<AuthOptions> auth,
    ILogger<TransactionalEmailService> logger) : ITransactionalEmailService
{
    private sealed record Recipient(Guid Id, string Email, string DisplayName, string? HomeCountry);

    public Task OrderShippedAsync(Order order, Shipment shipment, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var buyer = await RecipientAsync(order.BuyerUserId, ct);
            if (buyer is null)
            {
                return;
            }

            await SendAsync(
                buyer.Email,
                EmailTemplates.OrderShipped(
                    buyer.DisplayName,
                    order.OrderNumber,
                    shipment.CarrierName,
                    shipment.TrackingReference,
                    shipment.EstimatedArrivalUtc,
                    Url($"/dashboard/orders/{order.Id}")),
                ct);
        }, "order-shipped", ct);

    public Task QuotationReceivedAsync(
        TradeRequest rfq, Quotation quotation, Business supplier, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var buyer = await RecipientAsync(rfq.BuyerUserId, ct);
            if (buyer is null)
            {
                return;
            }

            await SendAsync(
                buyer.Email,
                EmailTemplates.NewQuotation(
                    buyer.DisplayName,
                    supplier.TradingName,
                    rfq.RfqNumber,
                    rfq.Title,
                    quotation.Total,
                    quotation.Currency,
                    quotation.LeadTimeDays,
                    quotation.ValidUntil,
                    Url($"/rfqs/{rfq.Id}")),
                ct);
        }, "quotation-received", ct);

    public Task PaymentInitiatedAsync(Payment payment, Order order, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var buyer = await RecipientAsync(order.BuyerUserId, ct);
            if (buyer is null)
            {
                return;
            }

            await SendAsync(
                buyer.Email,
                EmailTemplates.PaymentInitiated(
                    buyer.DisplayName,
                    order.OrderNumber,
                    payment.Reference,
                    payment.Amount,
                    payment.Currency,
                    payment.Method,
                    Url($"/dashboard/orders/{order.Id}")),
                ct);
        }, "payment-initiated", ct);

    public Task PaymentReceivedAsync(
        Payment payment, Order order, Guid? actorUserId = null, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            foreach (var recipient in await PartiesAsync(order, actorUserId, ct))
            {
                await SendAsync(
                    recipient.Email,
                    EmailTemplates.PaymentReceived(
                        recipient.DisplayName,
                        order.OrderNumber,
                        payment.Reference,
                        payment.Amount,
                        payment.Currency,
                        payment.Method,
                        Url($"/dashboard/orders/{order.Id}")),
                    ct);
            }
        }, "payment-received", ct);

    public Task PaymentFailedAsync(Payment payment, Order order, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var buyer = await RecipientAsync(order.BuyerUserId, ct);

            foreach (var recipient in await PartiesAsync(order, skipUserId: null, ct))
            {
                await SendAsync(
                    recipient.Email,
                    EmailTemplates.PaymentFailed(
                        recipient.DisplayName,
                        order.OrderNumber,
                        payment.Reference,
                        payment.Amount,
                        payment.Currency,
                        payment.FailureReason,
                        Url($"/dashboard/orders/{order.Id}")),
                    ct);
            }

            var buyerName = buyer?.DisplayName ?? "The buyer";
            foreach (var admin in await AdminsAsync(ct))
            {
                await SendAsync(
                    admin.Email,
                    EmailTemplates.AdminPaymentFailed(
                        admin.DisplayName,
                        order.OrderNumber,
                        payment.Reference,
                        payment.Amount,
                        payment.Currency,
                        buyerName,
                        payment.FailureReason,
                        Url("/admin/transactions")),
                    ct);
            }
        }, "payment-failed", ct);

    public Task PaymentRefundedAsync(
        Payment payment,
        Order order,
        decimal amount,
        string? reason,
        CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            foreach (var recipient in await PartiesAsync(order, skipUserId: null, ct))
            {
                await SendAsync(
                    recipient.Email,
                    EmailTemplates.PaymentRefunded(
                        recipient.DisplayName,
                        order.OrderNumber,
                        payment.Reference,
                        amount,
                        payment.Currency,
                        reason,
                        Url($"/dashboard/orders/{order.Id}")),
                    ct);
            }
        }, "payment-refunded", ct);

    public Task VerificationRequestedAsync(Business business, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var owner = await RecipientAsync(business.OwnerUserId, ct);

            foreach (var admin in await AdminsAsync(ct))
            {
                await SendAsync(
                    admin.Email,
                    EmailTemplates.AdminVerificationRequested(
                        admin.DisplayName,
                        business.TradingName,
                        owner?.DisplayName ?? "Unknown owner",
                        owner?.HomeCountry,
                        DateTime.UtcNow,
                        Url("/admin/verification")),
                    ct);
            }
        }, "verification-requested", ct);

    public Task DisputeOpenedAsync(Dispute dispute, CancellationToken ct = default) =>
        GuardAsync(async () =>
        {
            var orderNumber = await db.Orders
                .AsNoTracking()
                .Where(o => o.Id == dispute.OrderId)
                .Select(o => o.OrderNumber)
                .FirstOrDefaultAsync(ct);

            var raisedBy = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == dispute.RaisedByUserId)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);

            var against = await db.Businesses
                .AsNoTracking()
                .Where(b => b.Id == dispute.AgainstBusinessId)
                .Select(b => b.TradingName)
                .FirstOrDefaultAsync(ct);

            foreach (var admin in await AdminsAsync(ct))
            {
                await SendAsync(
                    admin.Email,
                    EmailTemplates.AdminDisputeOpened(
                        admin.DisplayName,
                        orderNumber ?? $"#{dispute.OrderId.ToString("N")[..8]}",
                        raisedBy ?? "A trader",
                        against ?? "A business",
                        dispute.Details,
                        Url("/admin/orders")),
                    ct);
            }
        }, "dispute-opened", ct);

    /// <summary>The buyer and the seller, minus whichever party performed the action.</summary>
    private async Task<IReadOnlyList<Recipient>> PartiesAsync(
        Order order, Guid? skipUserId, CancellationToken ct)
    {
        var recipients = new List<Recipient>();

        if (order.BuyerUserId != skipUserId && await RecipientAsync(order.BuyerUserId, ct) is { } buyer)
        {
            recipients.Add(buyer);
        }

        var seller = await SellerAsync(order, ct);
        if (seller is not null && seller.Id != skipUserId)
        {
            recipients.Add(seller);
        }

        return recipients;
    }

    private async Task<Recipient?> RecipientAsync(Guid userId, CancellationToken ct) =>
        await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && u.IsActive && u.EmailConfirmed && u.Email != null)
            .Select(u => new Recipient(u.Id, u.Email!, u.DisplayName, u.HomeCountry))
            .FirstOrDefaultAsync(ct);

    private async Task<Recipient?> SellerAsync(Order order, CancellationToken ct) =>
        await (from business in db.Businesses.AsNoTracking()
               join user in db.Users.AsNoTracking() on business.OwnerUserId equals user.Id
               where business.Id == order.SellerBusinessId
                   && user.IsActive
                   && user.EmailConfirmed
                   && user.Email != null
               select new Recipient(user.Id, user.Email!, user.DisplayName, user.HomeCountry))
            .FirstOrDefaultAsync(ct);

    /// <summary>Every active, confirmed administrator — the people who act on the alert.</summary>
    private async Task<IReadOnlyList<Recipient>> AdminsAsync(CancellationToken ct)
    {
        var roleId = await db.Roles
            .AsNoTracking()
            .Where(r => r.Name == RoleNames.Administrator)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(ct);

        if (roleId is null)
        {
            return [];
        }

        var adminRoleId = roleId.Value;

        return await (from userRole in db.UserRoles
                      join user in db.Users.AsNoTracking() on userRole.UserId equals user.Id
                      where userRole.RoleId == adminRoleId
                          && user.IsActive
                          && user.EmailConfirmed
                          && user.Email != null
                      select new Recipient(user.Id, user.Email!, user.DisplayName, user.HomeCountry))
            .ToListAsync(ct);
    }

    private string Url(string path) => $"{auth.Value.AppBaseUrl.TrimEnd('/')}{path}";

    private async Task SendAsync(string to, RenderedEmail rendered, CancellationToken ct)
    {
        try
        {
            await emailSender.SendAsync(
                new EmailMessage(
                    To: to,
                    Subject: rendered.Subject,
                    HtmlBody: rendered.Html,
                    PlainTextBody: rendered.Text,
                    InlineImages: rendered.InlineImages),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Failed to send '{Subject}' to {Recipient}.",
                rendered.Subject,
                EmailRedaction.Mask(to));
        }
    }

    /// <summary>
    /// Runs one notification without letting it escape into the already-committed business action.
    /// </summary>
    private async Task GuardAsync(Func<Task> action, string operation, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "The '{Operation}' email could not be prepared or sent.", operation);
        }
    }
}
