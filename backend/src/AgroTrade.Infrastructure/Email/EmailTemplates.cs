using System.Globalization;
using System.Net;
using System.Text;

using AgroTrade.Application.Common;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Infrastructure.Email;

/// <summary>A rendered email: subject, HTML and plain-text bodies, and the inline images the HTML links to.</summary>
public sealed record RenderedEmail(
    string Subject,
    string Html,
    string Text,
    IReadOnlyList<EmailInlineImage> InlineImages);

/// <summary>
/// The transactional emails: account flows (plan §04.4) and the marketplace events that follow a
/// shipment, quotation, payment or administrator queue. Styles are inline because mail clients
/// strip &lt;style&gt; blocks and external CSS, and every message carries a plain-text twin for
/// clients that refuse HTML. The markup is deliberately table-based — the one layout language every
/// client agrees on. The brand mark travels with the message (referenced by <c>cid:</c>) so the
/// header renders with no image host, and the wordmark is real text so the message still reads with
/// images blocked.
/// </summary>
public static class EmailTemplates
{
    private const string BrandName = "Achievers Global Consult";
    private const string BrandContactEmail = "info@achieversglobalconsult.com";
    private const string BrandMarkContentId = "achievers-brand-mark";
    private const string BrandMarkFileName = "achievers-global-consult.png";

    private const string BrandGreen = "#15803d";
    private const string ButtonGreen = "#16a34a";
    // brand-200, the light tint that keeps the "Global" accent legible on the green header bar.
    private const string AccentOnGreen = "#bbf7d0";
    private const string Ink = "#1c1917";
    private const string Muted = "#78716c";
    private const string Hairline = "#e7e5e4";
    private const string Page = "#f5f5f4";

    private static readonly byte[] BrandMarkPng = LoadBrandMark();

    private static readonly EmailInlineImage[] BrandMark =
    [
        new(BrandMarkContentId, BrandMarkFileName, "image/png", BrandMarkPng),
    ];

    public static RenderedEmail ConfirmEmail(string displayName, string confirmUrl, string token) =>
        Build(
            subject: $"Confirm your {BrandName} account",
            heading: "Confirm your email",
            greeting: displayName,
            intro: $"Thanks for joining {BrandName}. Confirm this address to activate your account and start trading.",
            actionLabel: "Confirm email",
            actionUrl: confirmUrl,
            token: token,
            outro: "The button signs you in and activates the account in one step. This link expires shortly; if it has, sign in and request a new confirmation email.");

    public static RenderedEmail PasswordResetCode(
        string displayName, string code, string resetUrl, int lifetimeMinutes) =>
        BuildCode(
            subject: $"Your {BrandName} password reset code",
            heading: "Reset your password",
            greeting: displayName,
            intro: "Enter the 6-digit code below to verify it’s you, then choose a new password — or open the link and we’ll verify it for you automatically.",
            code: code,
            actionLabel: "Verify & choose a new password",
            actionUrl: resetUrl,
            lifetimeMinutes: lifetimeMinutes,
            outro: $"This code expires in {lifetimeMinutes} minutes and can be used once. If you did not ask for this, you can ignore this email — your password stays unchanged.");

    /// <summary>
    /// The marketplace events. Unlike the auth messages these carry no secret: they confirm
    /// something that already happened and link the recipient to the record of it.
    /// </summary>
    public static RenderedEmail OrderShipped(
        string displayName,
        string orderNumber,
        string? carrierName,
        string? trackingReference,
        DateTime? estimatedArrivalUtc,
        string orderUrl)
    {
        var carrier = string.IsNullOrWhiteSpace(carrierName) ? "The seller" : carrierName.Trim();

        return Notice(
            subject: $"Your order {orderNumber} has shipped",
            heading: "Your order has shipped",
            greeting: displayName,
            intro: $"{carrier} has your consignment for order {orderNumber}. You can follow its progress from your dashboard.",
            rows:
            [
                ("Order", orderNumber),
                ("Carrier", carrierName),
                ("Tracking reference", trackingReference),
                ("Estimated arrival", estimatedArrivalUtc?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
            ],
            actionLabel: "Track your order",
            actionUrl: orderUrl,
            outro: "The order page carries the shipment timeline, trade documents and the latest delivery estimate.");
    }

    public static RenderedEmail NewQuotation(
        string displayName,
        string supplierName,
        string rfqNumber,
        string rfqTitle,
        decimal total,
        string currency,
        int leadTimeDays,
        DateTime? validUntilUtc,
        string rfqUrl) =>
        Notice(
            subject: $"New quotation on “{SubjectText(rfqTitle)}”",
            heading: "New quotation received",
            greeting: displayName,
            intro: $"{supplierName} has quoted on your RFQ “{rfqTitle}”. Review the offer and accept it when you’re ready.",
            rows:
            [
                ("RFQ", rfqNumber),
                ("Supplier", supplierName),
                ("Total", $"{total:N2} {currency}"),
                ("Lead time", leadTimeDays == 1 ? "1 day" : $"{leadTimeDays} days"),
                ("Valid until", validUntilUtc?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)),
            ],
            actionLabel: "Review the quotation",
            actionUrl: rfqUrl,
            outro: "You can compare every quotation, and the terms behind it, on the RFQ page.");

    public static RenderedEmail PaymentInitiated(
        string displayName,
        string orderNumber,
        string reference,
        decimal amount,
        string currency,
        PaymentMethod method,
        string orderUrl) =>
        Notice(
            subject: $"Payment initiated for order {orderNumber}",
            heading: "Payment initiated",
            greeting: displayName,
            intro: $"We’ve started payment {reference} for {amount:N2} {currency} on order {orderNumber}. We’ll email you again when the gateway confirms it.",
            rows:
            [
                ("Order", orderNumber),
                ("Payment reference", reference),
                ("Amount", $"{amount:N2} {currency}"),
                ("Method", PaymentMethodLabel(method)),
            ],
            actionLabel: "View order",
            actionUrl: orderUrl,
            outro: "If you did not start this payment, contact us immediately.");

    public static RenderedEmail PaymentReceived(
        string displayName,
        string orderNumber,
        string reference,
        decimal amount,
        string currency,
        PaymentMethod method,
        string orderUrl) =>
        Notice(
            subject: $"Payment received for order {orderNumber}",
            heading: "Payment received",
            greeting: displayName,
            intro: $"Payment {reference} for {amount:N2} {currency} has been recorded against order {orderNumber}.",
            rows:
            [
                ("Order", orderNumber),
                ("Payment reference", reference),
                ("Amount", $"{amount:N2} {currency}"),
                ("Method", PaymentMethodLabel(method)),
            ],
            actionLabel: "View order",
            actionUrl: orderUrl,
            outro: "The order page shows the updated balance and the payment receipt.");

    public static RenderedEmail PaymentFailed(
        string displayName,
        string orderNumber,
        string reference,
        decimal amount,
        string currency,
        string? reason,
        string orderUrl) =>
        Notice(
            subject: $"Payment failed for order {orderNumber}",
            heading: "Payment failed",
            greeting: displayName,
            intro: $"Payment {reference} for {amount:N2} {currency} on order {orderNumber} could not be completed.",
            rows:
            [
                ("Order", orderNumber),
                ("Payment reference", reference),
                ("Amount", $"{amount:N2} {currency}"),
                ("Reason", reason),
            ],
            actionLabel: "View order",
            actionUrl: orderUrl,
            outro: "The order is still outstanding. A new payment can be started from the order page.");

    public static RenderedEmail PaymentRefunded(
        string displayName,
        string orderNumber,
        string reference,
        decimal amount,
        string currency,
        string? reason,
        string orderUrl) =>
        Notice(
            subject: $"Refund issued for order {orderNumber}",
            heading: "Refund issued",
            greeting: displayName,
            intro: $"A refund of {amount:N2} {currency} has been issued on order {orderNumber}.",
            rows:
            [
                ("Order", orderNumber),
                ("Payment reference", reference),
                ("Amount", $"{amount:N2} {currency}"),
                ("Reason", reason),
            ],
            actionLabel: "View order",
            actionUrl: orderUrl,
            outro: "Refunds return through the original payment route; the buyer's bank or gateway may take a few days to show it.");

    public static RenderedEmail AdminVerificationRequested(
        string displayName,
        string businessName,
        string ownerName,
        string? homeCountry,
        DateTime requestedAtUtc,
        string adminUrl) =>
        Notice(
            subject: $"Admin alert: verification request from {SubjectText(businessName)}",
            heading: "Verification request",
            greeting: displayName,
            intro: $"{businessName} has submitted documents for verification.",
            rows:
            [
                ("Business", businessName),
                ("Owner", ownerName),
                ("Country", homeCountry),
                ("Submitted", requestedAtUtc.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture)),
            ],
            actionLabel: "Review in admin",
            actionUrl: adminUrl,
            outro: "Approve or reject the request from the verification queue.");

    public static RenderedEmail AdminDisputeOpened(
        string displayName,
        string orderNumber,
        string raisedBy,
        string against,
        string details,
        string adminUrl) =>
        Notice(
            subject: $"Admin alert: dispute opened on order {orderNumber}",
            heading: "Dispute opened",
            greeting: displayName,
            intro: $"A dispute has been opened on order {orderNumber}.",
            rows:
            [
                ("Order", orderNumber),
                ("Raised by", raisedBy),
                ("Against", against),
                ("Details", details),
            ],
            actionLabel: "Review in admin",
            actionUrl: adminUrl,
            outro: "The order, its payments and its documents remain available while the dispute is open.");

    public static RenderedEmail AdminPaymentFailed(
        string displayName,
        string orderNumber,
        string reference,
        decimal amount,
        string currency,
        string buyerName,
        string? reason,
        string adminUrl) =>
        Notice(
            subject: $"Admin alert: payment failed on order {orderNumber}",
            heading: "Payment failed",
            greeting: displayName,
            intro: $"Payment {reference} on order {orderNumber} failed.",
            rows:
            [
                ("Order", orderNumber),
                ("Payment reference", reference),
                ("Amount", $"{amount:N2} {currency}"),
                ("Buyer", buyerName),
                ("Reason", reason),
            ],
            actionLabel: "View transactions",
            actionUrl: adminUrl,
            outro: "Repeated failures on one corridor may need a gateway or account review.");

    private static string PaymentMethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.BankTransfer => "Bank transfer",
        PaymentMethod.LetterOfCredit => "Letter of credit",
        PaymentMethod.Gateway => "Card / gateway",
        _ => method.ToString(),
    };

    /// <summary>
    /// Keeps a user-supplied name out of the header structure. A trading name or RFQ title is not
    /// written by us, and a bare CR/LF in a Subject is a header-injection primitive; collapsing it
    /// to a space preserves the readable subject without letting it start a new header.
    /// </summary>
    private static string SubjectText(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>
    /// The reset message. It offers the two ways a user can prove they received it — type the
    /// six-digit code, or click the one-click link — so the code block and the button share equal
    /// billing rather than the button alone carrying the action.
    /// </summary>
    private static RenderedEmail BuildCode(
        string subject,
        string heading,
        string greeting,
        string intro,
        string code,
        string actionLabel,
        string actionUrl,
        int lifetimeMinutes,
        string outro)
    {
        var safeHeading = WebUtility.HtmlEncode(heading);
        var safeGreeting = WebUtility.HtmlEncode(greeting);
        var safeIntro = WebUtility.HtmlEncode(intro);
        var safeOutro = WebUtility.HtmlEncode(outro);
        var safeCode = WebUtility.HtmlEncode(code);
        var safeActionLabel = WebUtility.HtmlEncode(actionLabel);
        var safeUrl = WebUtility.HtmlEncode(actionUrl);

        var html = $"""
            <!doctype html>
            <html>
              <body style="margin:0;padding:0;background:{Page};font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};-webkit-font-smoothing:antialiased;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Page};padding:32px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid {Hairline};border-radius:16px;overflow:hidden;">
                        {Header()}
                        <tr>
                          <td style="padding:32px;">
                            <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;color:{Ink};">{safeHeading}</h1>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">Hi {safeGreeting},</p>
                            <p style="margin:0 0 24px;font-size:15px;line-height:1.6;">{safeIntro}</p>
                            <p style="margin:0 0 8px;font-size:13px;font-weight:600;color:{Muted};text-transform:uppercase;letter-spacing:0.08em;">Your reset code</p>
                            <p style="margin:0 0 24px;font-size:34px;font-weight:700;letter-spacing:0.35em;font-family:Consolas,Menlo,monospace;background:{Page};border:1px solid {Hairline};border-radius:10px;padding:18px;text-align:center;color:{BrandGreen};">{safeCode}</p>
                            <p style="margin:0 0 16px;font-size:13px;color:{Muted};">The code expires in {lifetimeMinutes} minutes — or tap the button to verify automatically and set a new password.</p>
                            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 20px;">
                              <tr>
                                <td style="border-radius:10px;background:{ButtonGreen};">
                                  <a href="{safeUrl}" style="display:inline-block;padding:12px 22px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:10px;">{safeActionLabel}</a>
                                </td>
                              </tr>
                            </table>
                            <p style="margin:0 0 8px;font-size:13px;color:{Muted};">If the button does not work, paste this link into your browser:</p>
                            <p style="margin:0 0 24px;font-size:13px;line-height:1.5;word-break:break-all;">
                              <a href="{safeUrl}" style="color:{BrandGreen};">{safeUrl}</a>
                            </p>
                            <p style="margin:0;font-size:13px;line-height:1.6;color:{Muted};">{safeOutro}</p>
                          </td>
                        </tr>
                        {Footer()}
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;

        var text = $"""
            {heading}

            Hi {greeting},

            {intro}

            Your code: {code}

            Or reset here: {actionUrl}

            {outro}

            —
            {BrandName} — importer & exporter of agro-allied products.
            Questions? {BrandContactEmail}
            """;

        return new RenderedEmail(subject, html, text, BrandMark);
    }

    private static RenderedEmail Build(
        string subject,
        string heading,
        string greeting,
        string intro,
        string actionLabel,
        string actionUrl,
        string token,
        string outro)
    {
        var safeHeading = WebUtility.HtmlEncode(heading);
        var safeGreeting = WebUtility.HtmlEncode(greeting);
        var safeIntro = WebUtility.HtmlEncode(intro);
        var safeActionLabel = WebUtility.HtmlEncode(actionLabel);
        var safeOutro = WebUtility.HtmlEncode(outro);
        var safeUrl = WebUtility.HtmlEncode(actionUrl);
        var safeToken = WebUtility.HtmlEncode(token);

        var html = $"""
            <!doctype html>
            <html>
              <body style="margin:0;padding:0;background:{Page};font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};-webkit-font-smoothing:antialiased;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Page};padding:32px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid {Hairline};border-radius:16px;overflow:hidden;">
                        {Header()}
                        <tr>
                          <td style="padding:32px;">
                            <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;color:{Ink};">{safeHeading}</h1>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">Hi {safeGreeting},</p>
                            <p style="margin:0 0 24px;font-size:15px;line-height:1.6;">{safeIntro}</p>
                            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
                              <tr>
                                <td style="border-radius:10px;background:{ButtonGreen};">
                                  <a href="{safeUrl}" style="display:inline-block;padding:12px 22px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:10px;">{safeActionLabel}</a>
                                </td>
                              </tr>
                            </table>
                            <p style="margin:0 0 8px;font-size:13px;color:{Muted};">If the button does not work, paste this link into your browser:</p>
                            <p style="margin:0 0 24px;font-size:13px;line-height:1.5;word-break:break-all;">
                              <a href="{safeUrl}" style="color:{BrandGreen};">{safeUrl}</a>
                            </p>
                            <p style="margin:0 0 8px;font-size:13px;color:{Muted};">Or enter this code by hand:</p>
                            <p style="margin:0 0 24px;font-size:14px;font-family:Consolas,Menlo,monospace;background:{Page};border:1px solid {Hairline};border-radius:8px;padding:12px;word-break:break-all;">{safeToken}</p>
                            <p style="margin:0;font-size:13px;line-height:1.6;color:{Muted};">{safeOutro}</p>
                          </td>
                        </tr>
                        {Footer()}
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;

        var text = $"""
            {heading}

            Hi {greeting},

            {intro}

            {actionLabel}: {actionUrl}

            Or enter this code by hand: {token}

            {outro}

            —
            {BrandName} — importer & exporter of agro-allied products.
            Questions? {BrandContactEmail}
            """;

        return new RenderedEmail(subject, html, text, BrandMark);
    }

    /// <summary>
    /// The shared layout for event notifications: a heading, a short intro, an optional
    /// label/value table and one action. Values are HTML-encoded because a trading name, a
    /// carrier or a dispute description is user-supplied text.
    /// </summary>
    private static RenderedEmail Notice(
        string subject,
        string heading,
        string greeting,
        string intro,
        IReadOnlyList<(string Label, string? Value)> rows,
        string actionLabel,
        string actionUrl,
        string outro)
    {
        var safeHeading = WebUtility.HtmlEncode(heading);
        var safeGreeting = WebUtility.HtmlEncode(greeting);
        var safeIntro = WebUtility.HtmlEncode(intro);
        var safeOutro = WebUtility.HtmlEncode(outro);
        var safeActionLabel = WebUtility.HtmlEncode(actionLabel);
        var safeUrl = WebUtility.HtmlEncode(actionUrl);

        var visibleRows = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Value))
            .ToList();

        var rowsHtml = new StringBuilder();
        for (var i = 0; i < visibleRows.Count; i++)
        {
            var divider = i == 0 ? string.Empty : $"border-top:1px solid {Hairline};";
            rowsHtml.Append($"""
                <tr>
                  <td style="padding:12px 16px;font-size:13px;color:{Muted};vertical-align:top;{divider}">{WebUtility.HtmlEncode(visibleRows[i].Label)}</td>
                  <td style="padding:12px 16px;font-size:14px;font-weight:600;text-align:right;vertical-align:top;{divider}">{WebUtility.HtmlEncode(visibleRows[i].Value)}</td>
                </tr>
                """);
        }

        var details = visibleRows.Count == 0
            ? string.Empty
            : $"""
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:0 0 24px;background:{Page};border:1px solid {Hairline};border-radius:10px;overflow:hidden;">
                  {rowsHtml}
                </table>
                """;

        var html = $"""
            <!doctype html>
            <html>
              <body style="margin:0;padding:0;background:{Page};font-family:Segoe UI,Roboto,Helvetica,Arial,sans-serif;color:{Ink};-webkit-font-smoothing:antialiased;">
                <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:{Page};padding:32px 16px;">
                  <tr>
                    <td align="center">
                      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid {Hairline};border-radius:16px;overflow:hidden;">
                        {Header()}
                        <tr>
                          <td style="padding:32px;">
                            <h1 style="margin:0 0 16px;font-size:22px;line-height:1.3;color:{Ink};">{safeHeading}</h1>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">Hi {safeGreeting},</p>
                            <p style="margin:0 0 24px;font-size:15px;line-height:1.6;">{safeIntro}</p>
                            {details}
                            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
                              <tr>
                                <td style="border-radius:10px;background:{ButtonGreen};">
                                  <a href="{safeUrl}" style="display:inline-block;padding:12px 22px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:10px;">{safeActionLabel}</a>
                                </td>
                              </tr>
                            </table>
                            <p style="margin:0;font-size:13px;line-height:1.6;color:{Muted};">{safeOutro}</p>
                          </td>
                        </tr>
                        {Footer()}
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;

        var textRows = string.Join(
            Environment.NewLine,
            visibleRows.Select(row => $"{row.Label}: {row.Value}"));

        var text = $"""
            {heading}

            Hi {greeting},

            {intro}

            {textRows}

            {actionLabel}: {actionUrl}

            {outro}

            —
            {BrandName} — importer & exporter of agro-allied products.
            Questions? {BrandContactEmail}
            """;

        return new RenderedEmail(subject, html, text, BrandMark);
    }

    /// <summary>
    /// The shared masthead: the brand-green bar carrying the embedded mark and the two-tone
    /// wordmark, mirroring the website header. The mark is a linked resource so it renders
    /// everywhere without a public URL; the wordmark beside it stays legible if images are off.
    /// </summary>
    private static string Header() => $"""
        <tr>
          <td style="background:{BrandGreen};padding:18px 32px;">
            <table role="presentation" cellpadding="0" cellspacing="0">
              <tr>
                <td style="padding-right:12px;">
                  <img src="cid:{BrandMarkContentId}" width="40" height="40" alt="{BrandName}" style="display:block;width:40px;height:40px;border:0;border-radius:10px;" />
                </td>
                <td style="font-size:18px;font-weight:700;letter-spacing:-0.01em;color:#ffffff;vertical-align:middle;">
                  Achievers <span style="color:{AccentOnGreen};">Global</span> Consult
                </td>
              </tr>
            </table>
          </td>
        </tr>
        """;

    /// <summary>The shared footer: the legal/brand line and a contact route, branded.</summary>
    private static string Footer() => $"""
        <tr>
          <td style="padding:20px 32px;border-top:1px solid {Hairline};font-size:12px;line-height:1.6;color:{Muted};">
            <p style="margin:0;">{BrandName} — importer &amp; exporter of agro-allied products.</p>
            <p style="margin:6px 0 0;">You received this email because this address was used on {BrandName}. Questions? <a href="mailto:{BrandContactEmail}" style="color:{BrandGreen};text-decoration:none;">{BrandContactEmail}</a></p>
          </td>
        </tr>
        """;

    /// <summary>
    /// Reads the brand mark from the assembly's embedded resources. Loaded once on first use; the
    /// asset is small (a 120&#215;120 PNG) so it can ride along with every message.
    /// </summary>
    private static byte[] LoadBrandMark()
    {
        var assembly = typeof(EmailTemplates).Assembly;
        var resourceName = Array.Find(
            assembly.GetManifestResourceNames(),
            name => name.EndsWith("brand-mark.png", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                "The embedded email brand mark (Email/Assets/brand-mark.png) was not found in the assembly.");

        using var resource = assembly.GetManifestResourceStream(resourceName)!;
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
