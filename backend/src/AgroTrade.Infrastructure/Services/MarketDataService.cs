using AgroTrade.Application.Common;
using AgroTrade.Application.Intelligence;
using AgroTrade.Domain.Intelligence;
using AgroTrade.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Services;

public sealed class MarketDataService(AgroTradeDbContext db) : IMarketDataService
{
    public async Task<PagedResult<MarketDataResponse>> ListAsync(MarketDataListRequest request, CancellationToken ct)
    {
        var query = db.MarketData.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Commodity))
        {
            query = query.Where(m => m.Commodity.ToLower() == request.Commodity.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(request.Country))
        {
            query = query.Where(m => m.Country == request.Country.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            query = query.Where(m => m.Currency == request.Currency.ToUpperInvariant());
        }

        if (request.From.HasValue)
        {
            query = query.Where(m => m.ReportedAtUtc >= request.From.Value);
        }

        if (request.To.HasValue)
        {
            query = query.Where(m => m.ReportedAtUtc <= request.To.Value);
        }

        query = request.Sort switch
        {
            "priceAsc" => query.OrderBy(m => m.Price),
            "priceDesc" => query.OrderByDescending(m => m.Price),
            "reportedAsc" => query.OrderBy(m => m.ReportedAtUtc),
            _ => query.OrderByDescending(m => m.ReportedAtUtc)
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(m => new MarketDataResponse(
                m.Id,
                m.Commodity,
                m.Country,
                m.Region,
                m.Unit,
                m.Price,
                m.Currency,
                m.Source,
                m.ReportedAtUtc,
                m.Notes))
            .ToListAsync(ct);

        return new PagedResult<MarketDataResponse>
        {
            Items = items,
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize
        };
    }

    public async Task<MarketDataResponse> CreateAsync(MarketDataCreateRequest request, CancellationToken ct)
    {
        var entity = new MarketData
        {
            Id = Guid.NewGuid(),
            Commodity = request.Commodity.Trim(),
            Country = request.Country.Trim().ToUpperInvariant(),
            Region = request.Region?.Trim(),
            Unit = request.Unit.Trim(),
            Price = request.Price,
            Currency = request.Currency.Trim().ToUpperInvariant(),
            Source = request.Source.Trim(),
            ReportedAtUtc = request.ReportedAtUtc,
            Notes = request.Notes?.Trim(),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        db.MarketData.Add(entity);
        await db.SaveChangesAsync(ct);

        return new MarketDataResponse(
            entity.Id,
            entity.Commodity,
            entity.Country,
            entity.Region,
            entity.Unit,
            entity.Price,
            entity.Currency,
            entity.Source,
            entity.ReportedAtUtc,
            entity.Notes);
    }
}