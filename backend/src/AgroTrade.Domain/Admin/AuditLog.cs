using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Admin;

/// <summary>
/// A record of one administrative action, for the admin activity log.
/// </summary>
/// <remarks>
/// <para>
/// Append-only, and deliberately narrow in scope: it captures what an <em>administrator</em> did,
/// not every write in the system. A generic change-tracking interceptor would also record the
/// incidental writes nobody would ever read - notification rows, audit-field stamps, shipment
/// milestones - and bury the handful of entries that matter under them.
/// </para>
/// <para>
/// This does not replace the domain's own records. A business verification review already writes
/// a <c>BusinessVerification</c> row and a document review already stamps
/// <c>ReviewedByUserId</c>/<c>ReviewedAtUtc</c>; both are the authoritative state. What this adds is
/// a single timeline spanning every kind of admin action, including those that leave no domain
/// trace of their own. "Who approved this business, when, and on what grounds" is otherwise
/// answerable only by querying each table separately and hoping the answer is still there.
/// </para>
/// <para>
/// Immutable for the same reason the shipment timeline is (task 2.6): an audit log that can be
/// edited is not evidence of anything.
/// </para>
/// </remarks>
public sealed class AuditLog : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The administrator who acted. Not nullable: an unattributable action is not a record.</summary>
    public Guid UserId { get; set; }

    /// <summary>What they did, e.g. <c>business.verification.review</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The table the action applied to, e.g. <c>Business</c>. Kept as text, not an enum,
    /// so a new admin action does not need a migration to be loggable.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The row acted on, when it has one. Null for platform-wide actions.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Short human-readable line for the log list, e.g. "Approved Delta Foods Ltd".</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The reason given, where the action took one. Rejection notes live here.</summary>
    public string? Details { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}