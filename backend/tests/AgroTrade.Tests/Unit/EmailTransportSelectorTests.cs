using AgroTrade.Infrastructure.Email;
using Microsoft.Extensions.Configuration;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// Which transport a configuration selects. The registration and the production guard both read
/// this answer, so the rules live in one place and are pinned here.
/// </summary>
public sealed class EmailTransportSelectorTests
{
    [Fact]
    public void WithNoConfiguration_UsesTheConsoleSink() =>
        Assert.Equal(EmailTransport.Console, Resolve());

    [Fact]
    public void WithAResendKey_ChoosesResend() =>
        Assert.Equal(EmailTransport.Resend, Resolve(resendKey: "re_test"));

    [Fact]
    public void WithAnSmtpHost_ChoosesSmtp() =>
        Assert.Equal(EmailTransport.Smtp, Resolve(smtpHost: "smtp.example.com"));

    [Fact]
    public void WithBoth_ResendWins() =>
        Assert.Equal(EmailTransport.Resend, Resolve(resendKey: "re_test", smtpHost: "smtp.example.com"));

    [Theory]
    [InlineData("Resend", EmailTransport.Resend)]
    [InlineData("resend", EmailTransport.Resend)]
    [InlineData("Smtp", EmailTransport.Smtp)]
    [InlineData("Console", EmailTransport.Console)]
    [InlineData("auto", EmailTransport.Console)]
    public void AnExplicitProviderIsHonoured(string provider, EmailTransport expected) =>
        Assert.Equal(expected, Resolve(provider: provider));

    [Theory]
    [InlineData("Postmark")]
    [InlineData("ses")]
    public void AnUnrecognisedProvider_IsReportedAsUnknown(string provider) =>
        Assert.Equal(EmailTransport.Unknown, Resolve(provider: provider));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Auto", true)]
    [InlineData("Resend", true)]
    [InlineData("smtp", true)]
    [InlineData("Console", true)]
    [InlineData("Postmark", false)]
    public void KnownProvidersAreRecognised(string? provider, bool known) =>
        Assert.Equal(known, EmailTransportSelector.IsKnownProvider(provider));

    private static EmailTransport Resolve(
        string? provider = null, string? resendKey = null, string? smtpHost = null)
    {
        var values = new Dictionary<string, string?>();
        if (provider is not null)
        {
            values["Email:Provider"] = provider;
        }

        if (resendKey is not null)
        {
            values["Email:Resend:ApiKey"] = resendKey;
        }

        if (smtpHost is not null)
        {
            values["Email:Smtp:Host"] = smtpHost;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return EmailTransportSelector.Resolve(configuration);
    }
}
