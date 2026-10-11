namespace AgroTrade.Application.Intelligence;

/// <summary>
/// Suggests the trade documents a route is likely to need, from the origin/destination pair
/// (docs/02-data-model.md — the "DestinationRequirements helper", Phase 4).
/// </summary>
/// <remarks>
/// This is a curated guide, not a legal determination: it encodes common corridor practice so a
/// first-time exporter has somewhere to start, and is deliberately a pure function over two
/// country codes so it can be served cheaply and read in a test without a database.
/// </remarks>
public interface IDestinationRequirementsService
{
    /// <param name="origin">ISO-3166 alpha-2 country the goods leave from.</param>
    /// <param name="destination">ISO-3166 alpha-2 country the goods are bound for.</param>
    DestinationRequirementsResponse Get(string origin, string destination);
}
