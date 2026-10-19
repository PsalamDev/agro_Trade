using AgroTrade.Domain.Trade;

namespace AgroTrade.Application.Common;

/// <summary>
/// Event-driven email: the messages a trader or administrator receives because something happened
/// in the marketplace, as opposed to the authentication flows that call <see cref="IEmailSender"/>
/// directly.
/// </summary>
/// <remarks>
/// Implementations resolve the recipients themselves, render the branded template and send. Sending
/// is best-effort: the business change has already been committed when these are called, so a
/// transport or lookup failure is logged and never surfaced as a failed operation.
/// </remarks>
public interface ITransactionalEmailService
{
    /// <summary>A consignment has been booked against an order; the buyer is told.</summary>
    Task OrderShippedAsync(Order order, Shipment shipment, CancellationToken ct = default);

    /// <summary>A supplier has quoted on an RFQ; the RFQ's buyer is told.</summary>
    Task QuotationReceivedAsync(
        TradeRequest rfq, Quotation quotation, Business supplier, CancellationToken ct = default);

    /// <summary>A gateway charge has been started; the buyer is told.</summary>
    Task PaymentInitiatedAsync(Payment payment, Order order, CancellationToken ct = default);

    /// <summary>
    /// Money has settled. Both parties are told except <paramref name="actorUserId"/>, the party
    /// whose own action produced the notification.
    /// </summary>
    Task PaymentReceivedAsync(
        Payment payment, Order order, Guid? actorUserId = null, CancellationToken ct = default);

    /// <summary>A gateway charge failed: both parties and the administrators are told.</summary>
    Task PaymentFailedAsync(Payment payment, Order order, CancellationToken ct = default);

    /// <summary>A refund has been issued; both parties are told.</summary>
    Task PaymentRefundedAsync(
        Payment payment, Order order, decimal amount, string? reason, CancellationToken ct = default);

    /// <summary>A business has entered the verification queue; the administrators are told.</summary>
    Task VerificationRequestedAsync(Business business, CancellationToken ct = default);

    /// <summary>A dispute has been opened; the administrators are told.</summary>
    Task DisputeOpenedAsync(Dispute dispute, CancellationToken ct = default);
}
