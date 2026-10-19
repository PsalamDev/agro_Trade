using System.Net;

namespace AgroTrade.Infrastructure.Email;

/// <summary>A delivered email: a subject plus the HTML and plain-text bodies a client may pick.</summary>
public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>
/// The auth-flow emails (plan §04.4). Styles are inline because mail clients strip &lt;style&gt;
/// blocks and external CSS, and every message carries a plain-text twin for clients that refuse
/// HTML. The markup is deliberately table-based — the one layout language every client agrees on.
/// </summary>
public static class EmailTemplates
{
    private const string BrandGreen = "#15803d";
    private const string ButtonGreen = "#16a34a";
    private const string Ink = "#1c1917";
    private const string Muted = "#78716c";
    private const string Hairline = "#e7e5e4";
    private const string Page = "#f5f5f4";

    public static RenderedEmail ConfirmEmail(string displayName, string confirmUrl, string token) =>
        Build(
            subject: "Confirm your AgroTrade account",
            heading: "Confirm your email",
            greeting: displayName,
            intro: "Thanks for joining AgroTrade. Confirm this address to activate your account and start trading.",
            actionLabel: "Confirm email",
            actionUrl: confirmUrl,
            token: token,
            outro: "The button signs you in and activates the account in one step. This link expires shortly; if it has, sign in and request a new confirmation email.");

    public static RenderedEmail PasswordResetCode(
        string displayName, string code, string resetUrl, int lifetimeMinutes) =>
        BuildCode(
            subject: "Your AgroTrade password reset code",
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
                        <tr>
                          <td style="background:{BrandGreen};padding:20px 32px;">
                            <span style="font-size:18px;font-weight:700;color:#ffffff;letter-spacing:-0.01em;">AgroTrade</span>
                          </td>
                        </tr>
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
                        <tr>
                          <td style="padding:20px 32px;border-top:1px solid {Hairline};font-size:12px;line-height:1.5;color:{Muted};">
                            AgroTrade — cross-border agricultural trade. You received this because this address was used on AgroTrade.
                          </td>
                        </tr>
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
            AgroTrade — cross-border agricultural trade.
            """;

        return new RenderedEmail(subject, html, text);
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
                        <tr>
                          <td style="background:{BrandGreen};padding:20px 32px;">
                            <span style="font-size:18px;font-weight:700;color:#ffffff;letter-spacing:-0.01em;">AgroTrade</span>
                          </td>
                        </tr>
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
                        <tr>
                          <td style="padding:20px 32px;border-top:1px solid {Hairline};font-size:12px;line-height:1.5;color:{Muted};">
                            AgroTrade — cross-border agricultural trade. You received this because this address was used on AgroTrade.
                          </td>
                        </tr>
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
            AgroTrade — cross-border agricultural trade.
            """;

        return new RenderedEmail(subject, html, text);
    }
}
