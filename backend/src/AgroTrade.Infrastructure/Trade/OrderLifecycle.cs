using AgroTrade.Domain.Enums;

namespace AgroTrade.Infrastructure.Trade;

/// <summary>Which side of the trade is acting on an order.</summary>
public enum OrderParty
{
    Buyer,
    Seller,
    /// <summary>Neither the buyer nor the selling business — not a participant.</summary>
    Outsider,
}

/// <summary>
/// The order lifecycle, declared once so the rules cannot drift between the endpoint, the
/// tests and the documentation (plan §02 — Orders).
///
/// <para>
/// Draft -> Placed -> Confirmed -> InTransit -> Delivered -> Completed, plus Cancelled from
/// any pre-shipment state. The buyer places the order and confirms receipt; the seller
/// confirms it and dispatches. Stock is reserved on placement, consumed on confirmation, and
/// released on cancellation.
/// </para>
/// </summary>
public static class OrderLifecycle
{
    /// <summary>Legal transitions, keyed by the state being left.</summary>
    private static readonly Dictionary<OrderStatus, (OrderStatus To, OrderParty[] Parties)[]> Rules = new()
    {
        [OrderStatus.Draft] =
        [
            (OrderStatus.Placed, [OrderParty.Buyer]),
            (OrderStatus.Cancelled, [OrderParty.Buyer, OrderParty.Seller]),
        ],
        [OrderStatus.Placed] =
        [
            (OrderStatus.Confirmed, [OrderParty.Seller]),
            (OrderStatus.Cancelled, [OrderParty.Buyer, OrderParty.Seller]),
        ],
        [OrderStatus.Confirmed] =
        [
            (OrderStatus.InTransit, [OrderParty.Seller]),
            (OrderStatus.Cancelled, [OrderParty.Buyer, OrderParty.Seller]),
        ],
        [OrderStatus.InTransit] =
        [
            // Once the goods have sailed, cancelling is no longer a clean operation — that
            // is a dispute, not a status change. Only arrival and delivery remain.
            (OrderStatus.Delivered, [OrderParty.Seller]),
        ],
        [OrderStatus.Delivered] =
        [
            (OrderStatus.Completed, [OrderParty.Buyer]),
        ],
        [OrderStatus.Completed] = [],
        [OrderStatus.Cancelled] = [],
    };

    /// <summary>States an order can no longer leave. Terminal states have no rules.</summary>
    public static bool IsTerminal(OrderStatus status) =>
        !Rules.TryGetValue(status, out var rules) || rules.Length == 0;

    public static bool IsLegal(OrderStatus from, OrderStatus to) =>
        Rules.TryGetValue(from, out var rules) && rules.Any(r => r.To == to);

    /// <summary>Whether the given party may drive the transition. Caller must also be a participant.</summary>
    public static bool CanPerform(OrderStatus from, OrderStatus to, OrderParty party) =>
        Rules.TryGetValue(from, out var rules)
        && rules.Any(r => r.To == to && r.Parties.Contains(party));

    /// <summary>Human-readable list of what may happen next, for error messages.</summary>
    public static string NextOptions(OrderStatus from)
    {
        if (!Rules.TryGetValue(from, out var rules) || rules.Length == 0)
        {
            return "none - this order is in a final state";
        }

        return string.Join(", ", rules.Select(r => r.To.ToString()));
    }

    /// <summary>
    /// Whether entering this state consumes reserved stock. Confirmation does, per plan §02
    /// ("reserve on order, decrement on confirm"); shipping and delivery do not, because the
    /// units already left the seller's stock at confirmation.
    /// </summary>
    public static bool CommitsStock(OrderStatus to) => to == OrderStatus.Confirmed;
}
