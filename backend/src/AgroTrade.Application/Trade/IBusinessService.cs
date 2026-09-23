using AgroTrade.Application.Common;

namespace AgroTrade.Application.Trade;

/// <summary>
/// Business lifecycle: register, update, trade-country reachability, KYC document
/// upload, verification workflow (submit → admin review → Verified badge), and
/// public search (plan §03 — Businesses).
/// </summary>
public interface IBusinessService
{
    Task<BusinessDetailDto> CreateAsync(Guid ownerUserId, RegisterBusinessRequest request, CancellationToken ct = default);

    Task<PagedResult<BusinessSummaryDto>> SearchAsync(BusinessSearchQuery query, CancellationToken ct = default);

    Task<BusinessDetailDto> GetAsync(Guid id, Guid? viewerUserId, bool viewerIsAdmin, CancellationToken ct = default);

    Task<BusinessDetailDto> UpdateAsync(Guid id, Guid ownerUserId, UpdateBusinessRequest request, CancellationToken ct = default);

    Task<BusinessDetailDto> SetTradeCountriesAsync(Guid id, Guid ownerUserId, TradeCountriesRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<BusinessSummaryDto>> MineAsync(Guid ownerUserId, CancellationToken ct = default);

    // ---- Documents ----
    Task<BusinessDocumentDto> UploadDocumentAsync(
        Guid id, Guid ownerUserId, int docType, string fileName, string contentType,
        Stream content, CancellationToken ct = default);

    Task<IReadOnlyList<BusinessDocumentDto>> GetDocumentsAsync(Guid id, Guid viewerUserId, bool isAdmin, CancellationToken ct = default);

    // ---- Verification ----
    Task<VerificationStateDto> SubmitForVerificationAsync(Guid id, Guid ownerUserId, CancellationToken ct = default);

    Task<VerificationStateDto> GetVerificationAsync(Guid id, Guid viewerUserId, bool isAdmin, CancellationToken ct = default);

    Task<BusinessDetailDto> ReviewVerificationAsync(Guid id, Guid adminUserId, ReviewVerificationRequest request, CancellationToken ct = default);

    Task<PagedResult<BusinessSummaryDto>> AdminSearchAsync(AdminBusinessSearchQuery query, CancellationToken ct = default);
}