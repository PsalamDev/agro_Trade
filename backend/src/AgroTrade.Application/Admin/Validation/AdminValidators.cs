using AgroTrade.Application.Admin;
using FluentValidation;

namespace AgroTrade.Application.Admin.Validation;

/// <summary>
/// Admin product moderation request (task 2.7).
/// </summary>
/// <remarks>
/// A body rather than a query string, and an enum rather than a bool, because the two actions are
/// not opposites of one another and neither is the absence of the other. Reinstate is not
/// "un-suspend by unpublishing", and expressing this as `?suspended=false` on the row itself is
/// exactly the arrangement that let a seller overwrite an administrator's decision in task 2.7.
/// </remarks>
public sealed record ModerateProductRequest
{
    /// <summary>Suspend or Reinstate.</summary>
    public ProductModerationAction Action { get; init; }

    /// <summary>Required when suspending. See the validator.</summary>
    public string? Reason { get; init; }
}

public sealed class ModerateProductRequestValidator : AbstractValidator<ModerateProductRequest>
{
    public ModerateProductRequestValidator()
    {
        RuleFor(x => x.Action)
            .IsInEnum()
            .WithMessage("Expected Suspend or Reinstate.");

        // Conditional, and deliberately only on Suspend. Suspension is the action that needs
        // justifying: it takes a working seller's revenue away, and a moderation record nobody can
        // explain is indistinguishable from an arbitrary one. Reinstating is the resolution, so a
        // note there is welcome context rather than the point of the call.
        RuleFor(x => x.Reason)
            .NotEmpty()
            .When(x => x.Action == ProductModerationAction.Suspend)
            .WithMessage("Suspending a listing requires a reason the seller can be shown.")
            .MaximumLength(400)
            .WithMessage("A moderation reason is limited to 400 characters.");
    }
}