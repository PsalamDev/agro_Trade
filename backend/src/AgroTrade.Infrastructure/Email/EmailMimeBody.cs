using AgroTrade.Application.Common;
using MimeKit;

namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// Builds the MIME body for a transactional message: an HTML body with a plain-text alternative,
/// where every <c>cid:</c> reference in the HTML resolves to an inline image carried on the
/// message. Split out from the SMTP transport so the structure can be asserted without a server.
/// </summary>
public static class EmailMimeBody
{
    public static MimeEntity Build(EmailMessage message)
    {
        var builder = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.PlainTextBody,
        };

        // Attach each image as a linked resource rather than pointing at a public URL: the image
        // then travels with the message and renders even in clients that block remote content.
        if (message.InlineImages is { Count: > 0 } images)
        {
            foreach (var image in images)
            {
                var part = (MimePart)builder.LinkedResources.Add(
                    image.FileName, image.Content, ContentType.Parse(image.ContentType));
                part.ContentId = image.ContentId;
            }
        }

        return builder.ToMessageBody();
    }
}
