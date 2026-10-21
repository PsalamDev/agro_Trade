using AgroTrade.Application.Common;
using AgroTrade.Infrastructure;
using AgroTrade.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// Which sender the container actually hands out. The transport is chosen from configuration, so
/// a deployment that sets a Resend key must end up talking to Resend rather than quietly falling
/// back to the log.
/// </summary>
public sealed class EmailTransportRegistrationTests
{
    [Fact]
    public void WithAResendKey_TheResendSenderIsRegistered()
    {
        var sender = Resolve(new Dictionary<string, string?>
        {
            ["Email:Resend:ApiKey"] = "re_test",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        });

        Assert.IsType<ResendEmailSender>(sender);
    }

    [Fact]
    public void WithAnSmtpHost_TheMailKitSenderIsRegistered()
    {
        var sender = Resolve(new Dictionary<string, string?>
        {
            ["Email:Smtp:Host"] = "smtp.example.com",
            ["Email:FromAddress"] = "no-reply@achieversglobalconsult.com",
        });

        Assert.IsType<SmtpEmailSender>(sender);
    }

    [Fact]
    public void WithNothing_TheConsoleSinkIsRegistered()
    {
        var sender = Resolve([]);

        Assert.IsType<ConsoleEmailSender>(sender);
    }

    [Fact]
    public void WithAnUnknownProvider_TheAppRefusesToStart()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<InvalidOperationException>(
            () => services.AddInfrastructure(Config(new Dictionary<string, string?>
            {
                ["Email:Provider"] = "Postmark",
            })));

        Assert.Contains("Email:Provider", error.Message);
    }

    [Fact]
    public void WithATransportButNoFromAddress_TheAppRefusesToStart()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(Config(new Dictionary<string, string?>
        {
            ["Email:Resend:ApiKey"] = "re_test",
        }));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var error = Assert.Throws<OptionsValidationException>(
            () => scope.ServiceProvider.GetRequiredService<IEmailSender>());

        Assert.Contains("Email:FromAddress", error.Message);
    }

    private static IEmailSender Resolve(Dictionary<string, string?> email)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(Config(email));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IEmailSender>();
    }

    private static IConfiguration Config(Dictionary<string, string?> email)
    {
        var values = new Dictionary<string, string?>(email)
        {
            ["ConnectionStrings:Default"] =
                "Server=localhost;Database=AgroTrade_Tests;Trusted_Connection=True;TrustServerCertificate=True;",
        };

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
