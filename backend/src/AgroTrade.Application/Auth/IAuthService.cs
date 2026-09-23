namespace AgroTrade.Application.Auth;

/// <summary>
/// Contract for the auth workflow (register, login, refresh, logout, confirm, resets).
/// Identity-backed implementation lives in AgroTrade.Infrastructure.
/// </summary>
public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken ct = default);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);

    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);

    Task LogoutAsync(string refreshToken, CancellationToken ct = default);

    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct = default);

    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);

    Task<AuthUserDto> GetCurrentUserAsync(Guid userId, CancellationToken ct = default);

    Task<AuthUserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct = default);
}