using AgroTrade.Application.Common;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// MVP email sink: writes to the log. Swap for a transactional provider (SendGrid,
/// Mailjet, resend…) behind IEmailSender at cloud deploy (plan D3).
/// </summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "[EMAIL-STUB] To: {To} | Subject: {Subject}\n{Body}",
            message.To, message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }
}