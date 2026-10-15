using AgroTrade.Application.Auth;
using AgroTrade.Application.Auth.Validation;

namespace AgroTrade.Tests.Unit;

public sealed class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Fact]
    public void ValidRequest_Passes()
    {
        var request = new RegisterRequest
        {
            Email = "trader@example.com",
            Password = "StrongPass1",
            DisplayName = "Mama Farms",
            HomeCountry = "KE",
            DefaultCurrency = "KES",
            Roles = [AgroTrade.Domain.Enums.RoleNames.Buyer],
        };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void WeakPassword_Fails()
    {
        var invalid = new[]
        {
            new RegisterRequest { Email = "a@b.com", Password = "short1A", DisplayName = "N" },
            new RegisterRequest { Email = "a@b.com", Password = "lowercaseonly1", DisplayName = "N" },
            new RegisterRequest { Email = "a@b.com", Password = "UPPERCASEONLY1", DisplayName = "N" },
            new RegisterRequest { Email = "a@b.com", Password = "NoDigitsHereA", DisplayName = "N" },
        };

        foreach (var request in invalid)
        {
            Assert.False(_validator.Validate(request).IsValid, $"Password '{request.Password}' should fail.");
        }
    }

    [Fact]
    public void InvalidRole_Fails()
    {
        var request = new RegisterRequest
        {
            Email = "a@b.com",
            Password = "StrongPass1",
            DisplayName = "N",
            Roles = [AgroTrade.Domain.Enums.RoleNames.Administrator], // not self-registrable
        };

        Assert.False(_validator.Validate(request).IsValid);
    }

    [Theory]
    [InlineData("NGN", true)] // 3 chars ok
    [InlineData("USD", true)]
    [InlineData("", false)]   // blank is rejected by .Length(3)
    [InlineData("NGNx", false)]
    public void Currency_Length(string currency, bool expectedValid)
    {
        var request = new RegisterRequest
        {
            Email = "a@b.com",
            Password = "StrongPass1",
            DisplayName = "N",
            DefaultCurrency = currency,
        };

        Assert.Equal(expectedValid, _validator.Validate(request).IsValid);
    }

    [Fact]
    public void BadCountryCode_Fails()
    {
        var request = new RegisterRequest
        {
            Email = "a@b.com",
            Password = "StrongPass1",
            DisplayName = "N",
            HomeCountry = "NGA",
        };

        Assert.False(_validator.Validate(request).IsValid);
    }
}