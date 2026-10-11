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

/// <summary>Admin account suspension/activation request.</summary>
/// <remarks>
/// An enum action rather than a boolean <c>isActive</c>, for the same reason moderation is an enum:
/// suspending and reactivating are not opposites of a state and neither is the absence of the other.
/// A bare boolean lets a stale client flip an account to Active while believing it is merely
/// "not changing anything", and it has nowhere to carry why the account was suspended.
/// </remarks>
public sealed record AdminUserStatusRequest
{
    /// <summary>Suspend or Activate.</summary>
    public UserStatusAction Action { get; init; }

    /// <summary>Required when suspending. See the validator.</summary>
    public string? Reason { get; init; }
}

public sealed class AdminUserStatusRequestValidator : AbstractValidator<AdminUserStatusRequest>
{
    public AdminUserStatusRequestValidator()
    {
        RuleFor(x => x.Action)
            .IsInEnum()
            .WithMessage("Expected Suspend or Activate.");

        // Suspension takes a working account away from its owner and out of the platform, so it is
        // the action that must be explained. Reactivating is the resolution, and a note there is
        // welcome context rather than the point of the call.
        RuleFor(x => x.Reason)
            .NotEmpty()
            .When(x => x.Action == UserStatusAction.Suspend)
            .WithMessage("Suspending an account requires a reason it can later be shown.")
            .MaximumLength(400)
            .WithMessage("A suspension reason is limited to 400 characters.");
    }
}
