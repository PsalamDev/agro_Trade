using System.Text.Json;

namespace AgroTrade.Application.Common;

/// <summary>
/// Helpers for the small JSON payloads on entities (certifications, packaging,
/// quality specs) stored as nvarchar(max) (plan §02 conventions).
/// </summary>
public static class JsonLists
{
    public static string ToJsonList(IEnumerable<string>? values) =>
        JsonSerializer.Serialize(values?.Distinct().ToList() ?? []);

    public static List<string> FromJsonList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string ToJsonDict(IReadOnlyDictionary<string, string>? values) =>
        JsonSerializer.Serialize(values ?? new Dictionary<string, string>());

    public static Dictionary<string, string> FromJsonDict(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}