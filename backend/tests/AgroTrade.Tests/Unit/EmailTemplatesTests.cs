using AgroTrade.Domain.Enums;
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

    [Fact]
    public void OrderShipped_NamesTheOrderAndCarrierAndLinksToIt()
    {
        var email = EmailTemplates.OrderShipped(
            "Ada Obi",
            "AG-2026-000042",
            "Maersk",
            "MSKU-778",
            new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc),
            "https://app.example.test/dashboard/orders/42");

        Assert.Contains("AG-2026-000042", email.Subject);
        Assert.Contains("Maersk", email.Html);
        Assert.Contains("MSKU-778", email.Html);
        Assert.Contains("03 Nov 2026", email.Html);
        Assert.Contains("https://app.example.test/dashboard/orders/42", email.Html);
        Assert.Contains("cid:achievers-brand-mark", email.Html);
        Assert.Contains("Order: AG-2026-000042", email.Text);
    }

    [Fact]
    public void NewQuotation_ShowsTheSupplierAndTheTerms()
    {
        var email = EmailTemplates.NewQuotation(
            "Ada Obi",
            "Sahel Grain Exporters",
            "RFQ-2026-000007",
            "Yellow maize, 500 tonnes",
            1250m,
            "USD",
            14,
            new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            "https://app.example.test/rfqs/7");

        Assert.Contains("Yellow maize, 500 tonnes", email.Subject);
        Assert.Contains("Sahel Grain Exporters", email.Html);
        Assert.Contains("RFQ-2026-000007", email.Html);
        Assert.Contains("14 days", email.Html);
        Assert.Contains("USD", email.Html);
        Assert.Contains("Supplier: Sahel Grain Exporters", email.Text);
    }

    [Fact]
    public void PaymentFailed_ShowsTheReasonAndTheRetryRoute()
    {
        var email = EmailTemplates.PaymentFailed(
            "Ada Obi",
            "AG-2026-000042",
            "PAY-2026-000019",
            1250m,
            "USD",
            "The card issuer declined the charge.",
            "https://app.example.test/dashboard/orders/42");

        Assert.Contains("Payment failed", email.Subject);
        Assert.Contains("The card issuer declined the charge.", email.Html);
        Assert.Contains("still outstanding", email.Html);
        Assert.Contains("PAY-2026-000019", email.Text);
    }

    [Fact]
    public void PaymentReceived_ShowsTheMethodInWords()
    {
        var email = EmailTemplates.PaymentReceived(
            "Ada Obi",
            "AG-2026-000042",
            "PAY-2026-000019",
            1250m,
            "USD",
            PaymentMethod.BankTransfer,
            "https://app.example.test/dashboard/orders/42");

        Assert.Contains("Payment received", email.Subject);
        Assert.Contains("Bank transfer", email.Html);
        Assert.Contains("PAY-2026-000019", email.Text);
    }

    [Fact]
    public void AdminAlerts_AreLabelledAndLinkToTheQueue()
    {
        var email = EmailTemplates.AdminVerificationRequested(
            "Platform Administrator",
            "Sahel Grain Exporters",
            "Ada Obi",
            "NG",
            new DateTime(2026, 10, 19, 9, 30, 0, DateTimeKind.Utc),
            "https://app.example.test/admin/verification");

        Assert.StartsWith("Admin alert:", email.Subject);
        Assert.Contains("Sahel Grain Exporters", email.Html);
        Assert.Contains("https://app.example.test/admin/verification", email.Html);
        Assert.Contains("Business: Sahel Grain Exporters", email.Text);
    }

    [Fact]
    public void Notice_EscapesUserSuppliedNames()
    {
        var email = EmailTemplates.NewQuotation(
            "Ada Obi",
            "<script>alert(1)</script>",
            "RFQ-2026-000007",
            "Yellow maize",
            1250m,
            "USD",
            14,
            null,
            "https://app.example.test/rfqs/7");

        Assert.DoesNotContain("<script>", email.Html);
        Assert.Contains("&lt;script&gt;", email.Html);
    }
}
