using AgroTrade.Domain.Common;

namespace AgroTrade.Domain.Admin;

/// <summary>
/// One operator-editable platform setting, stored as text and keyed by a catalogue entry.
/// </summary>
/// <remarks>
/// <para>
/// In the database rather than in <c>appsettings.json</c>, because these are the settings an
/// administrator changes while the platform is running. A JSON file is deployment configuration: to
/// flip one flag an operator would need a release, and the value would be identical on every
/// instance until that release rolled out. Persisting them means one write takes effect everywhere
/// on the next read, and the change is attributable to a person — which is the difference between a
/// setting and a rumour.
/// </para>
/// <para>
/// The row stores a string and nothing else. Its type lives in <c>PlatformSettingsCatalog</c>: the
/// catalogue decides what keys exist, what each means, and how its text is read. A table with a
/// free-text key and a free-text value and no catalogue in front of it is a place to hide typos,
/// not a settings system.
/// </para>
/// </remarks>
public sealed class PlatformSetting : IAuditableEntity
{
    /// <summary>Catalogue key, e.g. <c>registration.open</c>. Primary key: at most one row per setting.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The current value, serialised as text. Read back through the catalogue's parser rather than
    /// assumed to be any particular type.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// The administrator who last changed the value.
    /// </summary>
    /// <remarks>
    /// A plain column, not a foreign key to the user table, for the same reason
    /// <c>Transaction.PaymentId</c> is a correlation rather than a relationship: the setting, and
    /// the fact that somebody changed it, must survive that account being removed. A cascade would
    /// erase the attribution; a restrict would make the account undeletable.
    /// </remarks>
    public Guid? UpdatedByUserId { get; set; }

    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}
