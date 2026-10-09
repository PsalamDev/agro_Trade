namespace AgroTrade.Application.Trust;

public record ReviewResponse(
    Guid Id,
    Guid ReviewerUserId,
    Guid TargetBusinessId,
    Guid OrderId,
    byte Rating,
    string? Comment,
    bool IsPublished,
    DateTime CreatedAtUtc);

public record ReviewListRequest(
    int Page = 1,
    int PageSize = 20,
    Guid? TargetBusinessId = null,
    bool? IsPublished = true);

public record ReviewCreateRequest(
    Guid OrderId,
    Guid TargetBusinessId,
    byte Rating,
    string? Comment);