using AgroTrade.Application.Common;
using AgroTrade.Infrastructure.Email;
using MimeKit;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// The header logo only renders if the MIME body is shaped correctly: the HTML points at
/// <c>cid:achievers-brand-mark</c>, so it must sit beside an inline part carrying that exact
/// content id. This pins the wiring that a screenshot cannot.
/// </summary>
public sealed class EmailMimeBodyTests
{
    [Fact]
    public void Build_CarriesTheBrandMarkAsARelatedInlinePartMatchingTheHtmlCid()
    {
        var email = EmailTemplates.PasswordResetCode(
            "Ada Obi", "123456", "https://app.example.test/reset?code=123456", 10);

        var body = EmailMimeBody.Build(new EmailMessage(
            To: "ada@example.test",
            Subject: email.Subject,
            HtmlBody: email.Html,
            PlainTextBody: email.Text,
            InlineImages: email.InlineImages));

        // The plain-text alternative stays first, with the HTML (and its linked image) beside it.
        var alternative = Assert.IsType<MultipartAlternative>(body);
        var plain = Assert.Single(alternative.OfType<TextPart>());
        Assert.Equal("plain", plain.ContentType.MediaSubtype);

        var related = Assert.Single(alternative.OfType<MultipartRelated>());
        Assert.Equal("html", Assert.IsType<TextPart>(related.Root).ContentType.MediaSubtype);

        var image = Assert.Single(
            related.OfType<MimePart>(), part => part.ContentType.MediaType == "image");
        Assert.Equal("image/png", image.ContentType.MimeType);
        Assert.Equal("achievers-brand-mark", image.ContentId?.Trim('<', '>'));
        Assert.Equal(ContentDisposition.Inline, image.ContentDisposition?.Disposition);
    }
}
