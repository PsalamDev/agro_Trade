using AgroTrade.Application.Common;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// The no-configuration sink: written to the log instead of sent. Used whenever
/// <c>Email:Smtp:Host</c> is blank, which is the default for local development, and kept as the
/// deliberate fallback for a deployment that has not been given a mail server yet.
/// </summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogWarning(
            "[EMAIL-STUB] No Email:Smtp:Host configured — not sending. To: {To} | Subject: {Subject}\n{Body}",
            EmailRedaction.Mask(message.To), message.Subject, message.HtmlBody);
        return Task.CompletedTask;
    }
}
