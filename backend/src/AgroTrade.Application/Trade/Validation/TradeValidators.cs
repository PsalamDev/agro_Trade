using AgroTrade.Application.Communication;
using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;
using FluentValidation;

namespace AgroTrade.Application.Trade.Validation;

/// <summary>
/// DTO enum fields are ints (plan §02: stored as int, append-only). FluentValidation's
/// IsInEnum() calls Enum.IsDefined(typeof(int), …), which never succeeds — so check
/// against the real enum type instead.
/// </summary>
internal static class EnumChecks
{
    public static bool In<TEnum>(int value) where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value);

    /// <summary>Optional int enum field: null is valid, otherwise must be defined.</summary>
    public static bool IsNullOrIn<TEnum>(int? value) where TEnum : struct, Enum =>
        value is not int v || In<TEnum>(v);
}

public sealed class RegisterBusinessRequestValidator : AbstractValidator<RegisterBusinessRequest>
{
    public RegisterBusinessRequestValidator()
    {
        RuleFor(x => x.LegalName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.TradingName).NotEmpty().MaximumLength(250);
        RuleFor(x => x.BusinessType).Must(EnumChecks.In<BusinessType>);
        RuleFor(x => x.RegistrationNumber).MaximumLength(100);
        RuleFor(x => x.Website).MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(160)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").WithMessage("Slug must be lowercase kebab-case.");
        RuleFor(x => x.Icon).MaximumLength(50);
    }
}

public sealed class UpdateCategoryRequestValidator : AbstractValidator<UpdateCategoryRequest>
{
    public UpdateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(150).When(x => x.Name is not null);
        RuleFor(x => x.Slug).MaximumLength(160).When(x => x.Slug is not null)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$").WithMessage("Slug must be lowercase kebab-case.");
        RuleFor(x => x.Icon).MaximumLength(50).When(x => x.Icon is not null);
    }
}

public sealed class UpdateBusinessRequestValidator : AbstractValidator<UpdateBusinessRequest>
{
    public UpdateBusinessRequestValidator()
    {
        RuleFor(x => x.TradingName).MaximumLength(250).When(x => x.TradingName is not null);
        RuleFor(x => x.Website).MaximumLength(300).When(x => x.Website is not null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
    }
}

public sealed class ReviewVerificationRequestValidator : AbstractValidator<ReviewVerificationRequest>
{
    public ReviewVerificationRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(2000).When(x => x.Notes is not null);
        RuleFor(x => x).Must(x => x.Approve || !string.IsNullOrWhiteSpace(x.Notes))
            .WithMessage("Rejection requires a note explaining the reason.")
            .WithName(nameof(ReviewVerificationRequest.Notes));
    }
}

public sealed class TradeCountriesRequestValidator : AbstractValidator<TradeCountriesRequest>
{
    public TradeCountriesRequestValidator()
    {
        RuleFor(x => x.ExportsTo).Must(codes => codes.All(c => c.Length == 2))
            .WithMessage("ExportsTo countries must be ISO-3166 alpha-2 codes.")
            .Must(NoDuplicates).WithMessage("ExportsTo contains duplicate countries.");
        RuleFor(x => x.ImportsFrom).Must(codes => codes.All(c => c.Length == 2))
            .WithMessage("ImportsFrom countries must be ISO-3166 alpha-2 codes.")
            .Must(NoDuplicates).WithMessage("ImportsFrom contains duplicate countries.");
    }

    private static bool NoDuplicates(IEnumerable<string> codes)
    {
        var normalized = codes.Select(c => c.ToUpperInvariant()).ToList();
        return normalized.Count == normalized.Distinct().Count();
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OriginCountry).NotEmpty().Length(2);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AvailableQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PriceUnit).Must(EnumChecks.In<PriceUnit>);
        RuleFor(x => x.Incoterms).Must(EnumChecks.IsNullOrIn<Incoterms>);
        RuleFor(x => x.LeadTimeDays).GreaterThan(0).When(x => x.LeadTimeDays.HasValue);
        RuleFor(x => x.HsCode).MaximumLength(10);
        RuleFor(x => x.ListingStatus).Must(EnumChecks.In<ListingStatus>);
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Currency).Length(3).When(x => x.Currency is not null);
        RuleFor(x => x.Price).GreaterThanOrEqualTo(0).When(x => x.Price.HasValue);
        RuleFor(x => x.AvailableQuantity).GreaterThanOrEqualTo(0).When(x => x.AvailableQuantity.HasValue);
        RuleFor(x => x.MinimumOrderQuantity).GreaterThanOrEqualTo(0).When(x => x.MinimumOrderQuantity.HasValue);
        RuleFor(x => x.PriceUnit).Must(EnumChecks.IsNullOrIn<PriceUnit>);
        RuleFor(x => x.Incoterms).Must(EnumChecks.IsNullOrIn<Incoterms>);
        RuleFor(x => x.LeadTimeDays).GreaterThan(0).When(x => x.LeadTimeDays.HasValue);
        RuleFor(x => x.HsCode).MaximumLength(10).When(x => x.HsCode is not null);
        RuleFor(x => x.ListingStatus).Must(EnumChecks.IsNullOrIn<ListingStatus>);
    }
}

public sealed class CreateTradeRequestRequestValidator : AbstractValidator<CreateTradeRequestRequest>
{
    public CreateTradeRequestRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DestinationCountry).NotEmpty().Length(2);
        RuleFor(x => x.PreferredCurrency).NotEmpty().Length(3);
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Unit).Must(EnumChecks.In<PriceUnit>);
        RuleFor(x => x.PreferredIncoterm).Must(EnumChecks.IsNullOrIn<Incoterms>);
        RuleFor(x => x.TransportMode).Must(EnumChecks.IsNullOrIn<ShipmentMode>);
        RuleFor(x => x.PaymentTermsPreferred).Must(EnumChecks.IsNullOrIn<PaymentTerms>);
        RuleFor(x => x.ExpiresAt).GreaterThan(x => DateTime.UtcNow)
            .WithMessage("ExpiresAt must be in the future.");
        RuleFor(x => x.BudgetMax).GreaterThanOrEqualTo(x => x.BudgetMin)
            .When(x => x.BudgetMin.HasValue && x.BudgetMax.HasValue)
            .WithMessage("BudgetMax must be >= BudgetMin.");
    }
}

public sealed class CreateQuotationRequestValidator : AbstractValidator<CreateQuotationRequest>
{
    public CreateQuotationRequestValidator()
    {
        RuleFor(x => x.Currency).NotEmpty().Length(3);
        RuleFor(x => x.Incoterms).Must(EnumChecks.In<Incoterms>);
        RuleFor(x => x.PaymentTerms).Must(EnumChecks.In<PaymentTerms>);
        RuleFor(x => x.LeadTimeDays).GreaterThan(0);
        RuleFor(x => x.Total).GreaterThan(0);
    }
}

public sealed class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.ContextType).Must(EnumChecks.IsNullOrIn<MessageContextType>);
        RuleFor(x => x).Must(x => x.ContextId.HasValue == x.ContextType.HasValue)
            .WithMessage("ContextType and ContextId must be provided together.")
            .WithName(nameof(SendMessageRequest.ContextType));
    }
}