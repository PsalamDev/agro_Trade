namespace AgroTrade.Domain.Enums;

/// <summary>
/// Converts between the set of transport modes a logistics provider declares and the single
/// integer bitmask the database stores (Phase 4 — LSP directory).
/// </summary>
/// <remarks>
/// <see cref="ShipmentMode"/> is a small ordinal enum where <c>Road</c> is zero, so a naive bitmask
/// over its values would start at 1 and still fit comfortably in an <see cref="int"/>. The mapping
/// is centralised here rather than written at each call site because the provider row stores the
/// mask and the directory filters against it, and the two must agree bit for bit.
/// </remarks>
public static class ShipmentModeMask
{
    /// <summary>The bit standing for <paramref name="mode"/>.</summary>
    public static int Bit(ShipmentMode mode) => 1 << (int)mode;

    /// <summary>Folds a set of modes into one mask. Unknown values are ignored.</summary>
    public static int ToMask(IEnumerable<ShipmentMode>? modes)
    {
        var mask = 0;
        foreach (var mode in modes ?? [])
        {
            if (Enum.IsDefined(mode))
            {
                mask |= Bit(mode);
            }
        }

        return mask;
    }

    /// <summary>The modes named by <paramref name="mask"/>, in enum order.</summary>
    public static IReadOnlyList<ShipmentMode> FromMask(int mask) =>
        Enum.GetValues<ShipmentMode>()
            .Where(mode => (mask & Bit(mode)) != 0)
            .ToList();

    /// <summary>Whether <paramref name="mask"/> includes <paramref name="mode"/>.</summary>
    public static bool Includes(int mask, ShipmentMode mode) => (mask & Bit(mode)) != 0;
}
