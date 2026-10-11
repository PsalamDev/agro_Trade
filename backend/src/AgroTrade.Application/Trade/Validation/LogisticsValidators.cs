using AgroTrade.Domain.Enums;
using FluentValidation;

namespace AgroTrade.Application.Trade.Validation;

// Phase 4 — LSP directory + booking.

public sealed class UpsertLogisticsProviderRequestValidator : AbstractValidator<UpsertLogisticsProviderRequest>
{
    public UpsertLogisticsProviderRequestValidator()
    {
        RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.HomeCountry).NotEmpty().Length(2)
            .WithMessage("HomeCountry must be an ISO-3166 alpha-2 code.");

        RuleFor(x => x.ContactEmail).MaximumLength(200)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.ContactEmail));
        RuleFor(x => x.ContactPhone).MaximumLength(40);
        RuleFor(x => x.Website).MaximumLength(300);

        // A provider that handles no mode cannot be found by mode, and a provider that serves no
        // country cannot be found by country — so both are required rather than merely offered.
        RuleFor(x => x.Modes)
            .NotEmpty()
            .WithMessage("Select at least one transport mode.");
        RuleForEach(x => x.Modes)
            .Must(m => EnumChecks.In<ShipmentMode>((int)m))
            .WithMessage("Unrecognised transport mode.");

        RuleForEach(x => x.Services)
            .Must(s => s != LogisticsService.None && EnumChecks.In<LogisticsService>((int)s))
            .WithMessage("Unrecognised service.");

        RuleFor(x => x.Coverage)
            .NotEmpty()
            .WithMessage("Add at least one country you serve.");
        RuleForEach(x => x.Coverage).Length(2)
            .WithMessage("Each coverage country must be an ISO-3166 alpha-2 code.");
    }
}

public sealed class CreateLogisticsBookingRequestValidator : AbstractValidator<CreateLogisticsBookingRequest>
{
    public CreateLogisticsBookingRequestValidator()
    {
        RuleFor(x => x.ProviderId).NotEmpty()
            .WithMessage("A logistics provider is required.");
        RuleFor(x => x.Mode).Must(m => EnumChecks.In<ShipmentMode>((int)m));
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class RespondToLogisticsBookingRequestValidator : AbstractValidator<RespondToLogisticsBookingRequest>
{
    public RespondToLogisticsBookingRequestValidator()
    {
        RuleFor(x => x.Notes).MaximumLength(1000);
    }
}
