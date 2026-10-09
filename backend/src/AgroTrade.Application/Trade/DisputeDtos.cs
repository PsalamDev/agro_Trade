using AgroTrade.Domain.Enums;

namespace AgroTrade.Application.Trade;

public record DisputeResponse(
    Guid Id,
    Guid OrderId,
    Guid RaisedByUserId,
    Guid AgainstBusinessId,
    int Reason,
    string Details,
    DisputeStatus Status,
    Guid? ResolvedByUserId,
    string? Resolution,
    DateTime CreatedAtUtc,
    DateTime? ResolvedAtUtc);

public record DisputeCreateRequest(
    Guid OrderId,
    Guid AgainstBusinessId,
    int Reason,
    string Details);

public record DisputeResolveRequest(
    DisputeStatus Status,
    string? Resolution);