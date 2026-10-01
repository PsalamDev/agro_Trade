using AgroTrade.Application.Trade;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Infrastructure.Payments;

/// <summary>
/// Picks a gateway by capability, from whatever is registered at startup.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not a switch over currency and not a lookup keyed on
/// <see cref="PaymentProvider"/>. Provider values are append-only, so keying capability off
/// them would force one enum value per rail per currency — <c>SandboxUsd</c>,
/// <c>SandboxEur</c> — and make that permanent. Registering an adapter is the only way a
/// corridor opens.
/// </para>
/// <para>
/// First match wins, so where two adapters cover a currency the registration order decides.
/// That is a deployment decision, not something to resolve with cleverness here; with one
/// adapter today it cannot arise.
/// </para>
/// </remarks>
public sealed class PaymentGatewayResolver(IEnumerable<IPaymentGateway> gateways) : IPaymentGatewayResolver
{
    private readonly List<IPaymentGateway> gateways = gateways.ToList();

    public IPaymentGateway? Resolve(string currency)
    {
        var wanted = currency.ToUpperInvariant();

        return gateways.FirstOrDefault(g =>
            g.SupportedCurrencies.Contains(wanted, StringComparer.OrdinalIgnoreCase));
    }

    public IPaymentGateway? ResolveProvider(PaymentProvider provider) =>
        gateways.FirstOrDefault(g => g.Provider == provider);
}
