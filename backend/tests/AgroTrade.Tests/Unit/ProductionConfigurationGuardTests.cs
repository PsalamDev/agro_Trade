using AgroTrade.Api.Configuration;
using Microsoft.Extensions.Configuration;

namespace AgroTrade.Tests.Unit;

public sealed class ProductionConfigurationGuardTests
{
    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Valid() => new()
    {
        ["Email:Smtp:Host"] = "smtp.postmarkapp.com",
        ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        ["Auth:AutoConfirmEmail"] = "false",
        ["Auth:AppBaseUrl"] = "https://app.achieversglobalconsult.com",
        ["Cors:AllowedOrigins:0"] = "https://app.achieversglobalconsult.com",
    };

    [Fact]
    public void CompleteConfiguration_Passes()
    {
        Assert.Empty(ProductionConfigurationGuard.Validate(Config(Valid())));
    }

    [Theory]
    [InlineData("Email:Smtp:Host")]
    [InlineData("Email:FromAddress")]
    public void MissingEmailSetting_Fails(string key)
    {
        var values = Valid();
        values[key] = "";

        var errors = ProductionConfigurationGuard.Validate(Config(values));

        Assert.Contains(errors, error => error.Contains(key, StringComparison.Ordinal));
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
