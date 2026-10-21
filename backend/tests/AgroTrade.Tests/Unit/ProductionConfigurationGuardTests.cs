using AgroTrade.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace AgroTrade.Tests.Unit;

public sealed class ProductionConfigurationGuardTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>
    /// A production-shaped configuration with Resend as the transport. The email block can be
    /// replaced by a test to describe a different transport.
    /// </summary>
    private static Dictionary<string, string?> Valid(Dictionary<string, string?>? email = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Auth:AutoConfirmEmail"] = "false",
            ["Auth:AppBaseUrl"] = "https://app.achieversglobalconsult.com",
            ["Cors:AllowedOrigins:0"] = "https://app.achieversglobalconsult.com",
        };

        if (email is null)
        {
            values["Email:Resend:ApiKey"] = "re_test";
            values["Email:FromAddress"] = "no-reply@achieversglobalconsult.com";
        }
        else
        {
            foreach (var pair in email)
            {
                values[pair.Key] = pair.Value;
            }
        }

        return values;
    }

    [Fact]
    public void CompleteConfiguration_Passes()
    {
        Assert.Empty(ProductionConfigurationGuard.Validate(Config(Valid())));
    }

    [Fact]
    public void SmtpTransport_Passes()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "smtp.postmarkapp.com",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        })));

        Assert.Empty(errors);
    }

    [Fact]
    public void NoTransport_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        })));

        Assert.Contains(errors, error => error.Contains("Email:Resend:ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void ResendWithoutAnApiKey_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Resend",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        })));

        Assert.Contains(errors, error => error.Contains("Email:Resend:ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public void SmtpWithoutAHost_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Smtp",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        })));

        Assert.Contains(errors, error => error.Contains("Email:Smtp:Host", StringComparison.Ordinal));
    }

    [Fact]
    public void TheConsoleTransport_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Console",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        })));

        Assert.Contains(errors, error => error.Contains("No mail transport", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownProvider_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Postmark",
            ["Email:Resend:ApiKey"] = "re_test",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        })));

        Assert.Contains(errors, error => error.Contains("Email:Provider", StringComparison.Ordinal));
    }

    [Fact]
    public void AMissingFromAddress_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Resend:ApiKey"] = "re_test",
        })));

        Assert.Contains(errors, error => error.Contains("Email:FromAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void AFromAddressThatIsNotAnEmail_Fails()
    {
        var errors = ProductionConfigurationGuard.Validate(Config(Valid(new Dictionary<string, string?>
        {
            ["Email:Resend:ApiKey"] = "re_test",
            ["Email:FromAddress"] = "no-reply",
        })));

        Assert.Contains(errors, error => error.Contains("Email:FromAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void AutoConfirmEmail_Fails()
    {
        var values = Valid();
        values["Auth:AutoConfirmEmail"] = "true";

        var errors = ProductionConfigurationGuard.Validate(Config(values));

        Assert.Contains(errors, error => error.Contains("AutoConfirmEmail", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://app.example.com")]
    [InlineData("https://localhost:5173")]
    [InlineData("https://127.0.0.1:5173")]
    [InlineData("not-a-url")]
    public void NonPublicAppBaseUrl_Fails(string value)
    {
        var values = Valid();
        values["Auth:AppBaseUrl"] = value;

        var errors = ProductionConfigurationGuard.Validate(Config(values));

        Assert.Contains(errors, error => error.Contains("Auth:AppBaseUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("http://app.example.com")]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:5173")]
    public void NonHttpsOrLocalCorsOrigin_Fails(string origin)
    {
        var values = Valid();
        values["Cors:AllowedOrigins:0"] = origin;

        var errors = ProductionConfigurationGuard.Validate(Config(values));

        Assert.Contains(errors, error => error.Contains("Cors:AllowedOrigins", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyCorsOrigins_Fails()
    {
        var values = Valid();
        values.Remove("Cors:AllowedOrigins:0");

        var errors = ProductionConfigurationGuard.Validate(Config(values));

        Assert.Contains(errors, error => error.Contains("Cors:AllowedOrigins", StringComparison.Ordinal));
    }
}
