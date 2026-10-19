namespace AgroTrade.Application.Common;

/// <summary>
/// An image carried inside the message body rather than fetched from the internet. The HTML refers
/// to it as <c>cid:{ContentId}</c>; mail clients display it even when they block remote images.
/// </summary>
public sealed record EmailInlineImage(
    string ContentId,
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? PlainTextBody = null,
    IReadOnlyList<EmailInlineImage>? InlineImages = null);

/// <summary>
/// Outbound email contract. MVP implementation logs to console; a transactional
/// provider is swapped in behind this interface at cloud deploy (see plan D3).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
