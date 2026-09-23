using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Registration, login, refresh-token rotation, logout, email confirmation, and
/// password reset (plan §03 — Auth & Identity). Rate-limited as a brute-force guard.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting("auth")]
public class AuthController(
    IAuthService authService,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<RefreshRequest> refreshValidator,
    IValidator<ResetPasswordRequest> resetValidator) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await registerValidator.ValidateAndThrowAsync(request, ct);
        var response = await authService.RegisterAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await loginValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await authService.LoginAsync(request, ct));
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await refreshValidator.ValidateAndThrowAsync(request, ct);
        return Ok(await authService.RefreshAsync(request, ct));
    }

    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await authService.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await authService.ConfirmEmailAsync(request, ct);
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await authService.ForgotPasswordAsync(request, ct);
        return NoContent();
    }

    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken ct)
    {
        EnsureBody(request);
        await resetValidator.ValidateAndThrowAsync(request, ct);
        await authService.ResetPasswordAsync(request, ct);
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken ct)
    {
        var userId = User.GetUserId();
        return Ok(await authService.GetCurrentUserAsync(userId, ct));
    }

    private static void EnsureBody(object? request)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }
    }
}