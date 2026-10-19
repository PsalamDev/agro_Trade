using AgroTrade.Infrastructure.Email;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// The transactional emails are the first surface a trader sees from the company, so the branding
/// has to survive the mail client: the header carries the embedded mark and wordmark, the copy
/// names the company, and the plain-text twin stays readable for text-only clients.
/// </summary>
public sealed class EmailTemplatesTests
{
    private const string ResetUrl = "https://app.achieversglobalconsult.com/reset-password?email=a%40b.test&code=123456&auto=1";

    [Fact]
    public void PasswordResetCode_NamesTheCompanyInSubjectAndBody()
    {
        var email = EmailTemplates.PasswordResetCode("Ada Obi", "123456", ResetUrl, 10);

        Assert.Contains("Achievers Global Consult", email.Subject);
        Assert.Contains("Achievers Global Consult", email.Html);
        Assert.DoesNotContain("AgroTrade", email.Subject + email.Html + email.Text);
    }

    [Fact]
    public void PasswordResetCode_EmbedsTheMarkAsAnInlineImageTheHeaderLinksTo()
    {
        var email = EmailTemplates.PasswordResetCode("Ada Obi", "123456", ResetUrl, 10);

        Assert.Contains("cid:achievers-brand-mark", email.Html);

        var image = Assert.Single(email.InlineImages);
        Assert.Equal("achievers-brand-mark", image.ContentId);
        Assert.Equal("image/png", image.ContentType);
        // The PNG signature, so a real image rode along rather than an empty placeholder.
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.Content[..4]);
    }

    [Fact]
    public void Header_ShowsTheFullBrandNameBesideTheMark()
    {
        var email = EmailTemplates.PasswordResetCode("Ada Obi", "123456", ResetUrl, 10);

        // "Global" sits in an accent span, so assert the visible wordmark ends with "Consult".
        Assert.Matches(@"Achievers\s+<span[^>]*>Global</span>\s+Consult", email.Html);
    }

    [Fact]
    public void PasswordResetCode_KeepsThePlainTextTwinReadable()
    {
        // The integration suite, and text-only clients, read the code from the plain-text twin.
        var email = EmailTemplates.PasswordResetCode("Ada Obi", "123456", ResetUrl, 10);

        Assert.Contains("Your code: 123456", email.Text);
        Assert.Contains("Achievers Global Consult", email.Text);
    }

    [Fact]
    public void ConfirmEmail_IsBrandedToo()
    {
        var email = EmailTemplates.ConfirmEmail("Ada Obi", "https://app.example.test/confirm-email?token=tok", "tok");

        Assert.Contains("Achievers Global Consult", email.Subject);
        Assert.Contains("cid:achievers-brand-mark", email.Html);
        Assert.DoesNotContain("AgroTrade", email.Subject + email.Html + email.Text);
        Assert.Single(email.InlineImages);
    }
}
