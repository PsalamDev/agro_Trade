namespace AgroTrade.Application.Common;

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null);

/// <summary>
/// Outbound email contract. MVP implementation logs to console; a transactional
/// provider is swapped in behind this interface at cloud deploy (see plan D3).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}