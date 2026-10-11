using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Admin;

/// <summary>How a setting's text is read and validated.</summary>
public enum PlatformSettingKind
{
    Boolean = 0,
    Role = 1,
}

/// <summary>
/// One setting an operator may change: its key, its human label, its type, and the value used when
/// no row has been written.
/// </summary>
/// <remarks>
/// The catalogue is the settings system. The table holds a key and a string; this list is what makes
/// a key meaningful, and what lets the API reject a key nobody wrote an interpretation for. Without
/// it, <c>GET /admin/settings</c> would be an empty screen and <c>PUT</c> a way to write rows no
/// code ever reads.
/// </remarks>
public sealed record PlatformSettingDefinition(
    string Key,
    string Label,
    string Description,
    PlatformSettingKind Kind,
    string DefaultValue);

/// <summary>
/// The settings the platform recognises, and the readers for their values.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small and entirely load-bearing. Every key here is read by running code — a setting
/// that no code consults is documentation wearing a form field, and worse than nothing, because an
/// operator who changes it will believe something happened. Adding a key means adding its reader.
/// </para>
/// <para>
/// This is not a substitute for deployment configuration. Connection strings, signing keys and
/// feature toggles that must match a build stay in <c>appsettings</c>; what lives here is the small
/// set of operational choices an administrator changes while the platform is running, without a
/// release.
/// </para>
/// </remarks>
public static class PlatformSettingsCatalog
{
    /// <summary>Whether new accounts can be created. Closing registration is the platform's emergency brake.</summary>
    public const string RegistrationOpenKey = "registration.open";

    /// <summary>
    /// The role assigned to a registrant who does not choose one.
    /// </summary>
    /// <remarks>
    /// Separate from the roles a caller may request, which are always validated against
    /// <see cref="RoleNames.Registrable"/>. This decides the fallback, not what is permitted.
    /// </remarks>
    public const string RegistrationDefaultRoleKey = "registration.default_role";

    public static readonly IReadOnlyList<PlatformSettingDefinition> Definitions =
    [
        new(
            RegistrationOpenKey,
            "Registration open",
            "When off, new accounts cannot be created. Existing users sign in and trade as normal.",
            PlatformSettingKind.Boolean,
            "true"),
        new(
            RegistrationDefaultRoleKey,
            "Default registration role",
            "The role given to a new account that does not choose one. Must be Buyer, Importer or Distributor.",
            PlatformSettingKind.Role,
            RoleNames.Buyer),
    ];

    /// <summary>The definition for a key, or null if the platform has no such setting.</summary>
    public static PlatformSettingDefinition? Find(string key) =>
        Definitions.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Validates a candidate value for a definition. Returns null when it is acceptable, otherwise a
    /// user-safe reason an administrator can act on.
    /// </summary>
    public static string? Validate(PlatformSettingDefinition definition, string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return $"A value is required for \"{definition.Label}\".";
        }

        switch (definition.Kind)
        {
            case PlatformSettingKind.Boolean:
                return bool.TryParse(trimmed, out _)
                    ? null
                    : $"\"{definition.Label}\" must be true or false.";

            case PlatformSettingKind.Role:
                // A registrable role only. Allowing "Company" here would hand a self-registered
                // buyer the supplier role the single-supplier model deliberately withholds.
                return FindRole(trimmed) is not null
                    ? null
                    : $"\"{trimmed}\" is not a registrable role (expected one of: {string.Join(", ", RoleNames.Registrable)}).";

            default:
                return "Unknown setting type.";
        }
    }

    /// <summary>
    /// Canonical form of a value before it is stored: booleans lower-cased so <c>True</c> and
    /// <c>true</c> are not two different settings, and roles matched to the platform's own casing so
    /// <c>buyer</c> is stored as <c>Buyer</c>.
    /// </summary>
    public static string Normalize(PlatformSettingDefinition definition, string value)
    {
        var trimmed = value.Trim();
        return definition.Kind switch
        {
            PlatformSettingKind.Boolean => trimmed.ToLowerInvariant(),
            PlatformSettingKind.Role => FindRole(trimmed) ?? trimmed,
            _ => trimmed,
        };
    }

    /// <summary>
    /// Reads a boolean setting, falling back to the supplied default when no row exists. An
    /// unparseable stored value also falls back, rather than throwing: a bad row must not be able to
    /// take the read path down, and the fallback is the documented, safe value.
    /// </summary>
    public static bool ReadBoolean(string? stored, bool fallback) =>
        bool.TryParse(stored?.Trim(), out var parsed) ? parsed : fallback;

    /// <summary>
    /// Reads a role setting, falling back when nothing is stored or the stored role is no longer
    /// registrable. A role removed from the registrable set must not keep being handed out by a
    /// stale setting row.
    /// </summary>
    public static string ReadRole(string? stored, string fallback) =>
        FindRole(stored?.Trim()) ?? fallback;

    /// <summary>Case-insensitive lookup of a registrable role name, returning the canonical casing.</summary>
    private static string? FindRole(string? value) =>
        value is null
            ? null
            : RoleNames.Registrable.FirstOrDefault(r => string.Equals(r, value, StringComparison.OrdinalIgnoreCase));
}
