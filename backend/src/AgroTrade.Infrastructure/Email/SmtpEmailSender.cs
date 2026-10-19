using AgroTrade.Application.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// SMTP delivery through MailKit (plan D3). Registered only when <c>Email:Smtp:Host</c> is set;
/// otherwise <see cref="ConsoleEmailSender"/> stays in place so local development needs no server.
/// </summary>
public sealed class SmtpEmailSender(
    IOptions<EmailOptions> options,
    ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var smtp = _options.Smtp;
        var from = _options.FromAddress
            ?? throw new InvalidOperationException(
                "Email:FromAddress must be set when Email:Smtp:Host is configured.");

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.PlainTextBody,
        }.ToMessageBody();

        using var client = new SmtpClient { Timeout = smtp.TimeoutSeconds * 1000 };

        await client.ConnectAsync(smtp.Host!, smtp.Port, ParseSecureSocket(smtp.SecureSocket), ct);
        if (!string.IsNullOrWhiteSpace(smtp.Username))
        {
            var password = smtp.Password ?? string.Empty;
            await client.AuthenticateAsync(smtp.Username, password, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);

        logger.LogInformation(
            "Sent '{Subject}' to {Recipient} via {Host}:{Port}.",
            message.Subject, EmailRedaction.Mask(message.To), smtp.Host, smtp.Port);
    }

    /// <summary>
    /// Falls back to <see cref="SecureSocketOptions.Auto"/> for an unrecognised value rather than
    /// failing: Auto is the safe negotiation and the alternative is a startup crash over a typo.
    /// </summary>
    private static SecureSocketOptions ParseSecureSocket(string? value) =>
        Enum.TryParse<SecureSocketOptions>(value, ignoreCase: true, out var parsed)
            ? parsed
            : SecureSocketOptions.Auto;
}
