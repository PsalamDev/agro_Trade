using System.Net;

using AgroTrade.Application.Common;

namespace AgroTrade.Infrastructure.Email;

/// <summary>A rendered email: subject, HTML and plain-text bodies, and the inline images the HTML links to.</summary>
public sealed record RenderedEmail(
    string Subject,
    string Html,
    string Text,
    IReadOnlyList<EmailInlineImage> InlineImages);

/// <summary>
/// The auth-flow emails (plan §04.4). Styles are inline because mail clients strip &lt;style&gt;
/// blocks and external CSS, and every message carries a plain-text twin for clients that refuse
/// HTML. The markup is deliberately table-based — the one layout language every client agrees on.
/// The brand mark travels with the message (referenced by <c>cid:</c>) so the header renders with
/// no image host, and the wordmark is real text so the message still reads with images blocked.
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
