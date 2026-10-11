using AgroTrade.Application.Common;
using AgroTrade.Application.Intelligence;
using AgroTrade.Domain.Enums;

namespace AgroTrade.Infrastructure.Services;

/// <summary>
/// Curated corridor rules behind <see cref="IDestinationRequirementsService"/>. Pure and stateless:
/// the answer is a function of the two country codes alone, so it is served without touching the
/// database.
/// </summary>
public sealed class DestinationRequirementsService : IDestinationRequirementsService
{
    // Agricultural exporters. Their produce consignments need plant-health certification, which is
    // issued where the goods originate rather than where they land.
    private static readonly HashSet<string> AgriculturalOrigins = new(StringComparer.Ordinal)
    {
        "NG", "GH", "KE", "ZA", "CI", "SN", "CM", "ET", "TZ", "UG", "RW", "ZM", "MW", "BF", "ML",
        "BJ", "TG",
    };

    // The EU/UK/Schengen-adjacent destination bloc: border control posts sample plant and food
    // imports and expect plant-health paperwork.
    private static readonly HashSet<string> EuropeanDestinations = new(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "HR", "CY", "CZ", "DK", "EE", "FI", "FR", "DE", "GR", "HU", "IE", "IT",
        "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE", "GB", "CH", "NO",
        "IS",
    };

    // Gulf states that gate imports behind a permit raised in the destination country.
    private static readonly HashSet<string> GulfDestinations = new(StringComparer.Ordinal)
    {
        "AE", "SA", "QA", "KW", "OM", "BH",
    };

    public DestinationRequirementsResponse Get(string origin, string destination)
    {
        if (!IsCountryCode(origin) || !IsCountryCode(destination))
        {
            throw ApiException.BadRequest(
                "INVALID_COUNTRY",
                "Provide origin and destination as ISO-3166 alpha-2 country codes.");
        }

        var from = origin.Trim().ToUpperInvariant();
        var to = destination.Trim().ToUpperInvariant();

        // Moving goods within one country is not an export: no customs documents apply.
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return new DestinationRequirementsResponse
            {
                Origin = from,
                Destination = to,
                Domestic = true,
            };
        }

        var documents = new SortedDictionary<DocumentType, DestinationRequirementDto>();
        var notes = new List<string>();

        void Add(DocumentType document, bool mandatory, string reason)
        {
            // Once a document is on the list as mandatory, a later suggested mention must not
            // downgrade it — the strongest requirement wins.
            if (documents.TryGetValue(document, out var existing) && (existing.Mandatory || !mandatory))
            {
                return;
            }

            documents[document] = new DestinationRequirementDto
            {
                Document = document,
                Mandatory = mandatory,
                Reason = reason,
            };
        }

        Add(DocumentType.CommercialInvoice, true, "Always required to clear customs and settle duty.");
        Add(DocumentType.PackingList, true, "Itemises the consignment for customs and the carrier.");
        Add(DocumentType.CertificateOfOrigin, true, "Establishes origin for duty and preferential treatment.");
        Add(DocumentType.BillOfLading, false, "Carrier transport document — a bill of lading by sea, an air waybill by air.");

        if (AgriculturalOrigins.Contains(from))
        {
            Add(
                DocumentType.PhytosanitaryCertificate,
                true,
                "Plant and produce consignments must be certified pest-free at origin.");
        }

        if (EuropeanDestinations.Contains(to))
        {
            Add(
                DocumentType.PhytosanitaryCertificate,
                false,
                "EU/UK border controls expect plant-health certification for most produce.");
            Add(
                DocumentType.InspectionCertificate,
                false,
                "Consignments may be sampled at a border control post on arrival.");
            notes.Add(
                "EU and UK imports of plant products are subject to border checks and generally need a "
                + "phytosanitary certificate.");
        }
        else if (GulfDestinations.Contains(to))
        {
            Add(
                DocumentType.ImportPermit,
                true,
                "Gulf importers must hold a permit raised in the destination country before dispatch.");
            Add(
                DocumentType.InspectionCertificate,
                false,
                "Consignments are inspected and certified as conforming on arrival.");
            notes.Add(
                "Gulf states require an import permit issued in the destination country before the goods ship.");
        }
        else if (string.Equals(to, "NG", StringComparison.Ordinal))
        {
            Add(
                DocumentType.ImportPermit,
                true,
                "Nigeria requires an import permit raised before the goods ship.");
        }

        return new DestinationRequirementsResponse
        {
            Origin = from,
            Destination = to,
            Domestic = false,
            Documents = documents.Values.ToList(),
            Notes = notes.Count == 0 ? null : string.Join(" ", notes),
        };
    }

    private static bool IsCountryCode(string? code) =>
        code is { Length: 2 } && code.All(char.IsAsciiLetter);
}
