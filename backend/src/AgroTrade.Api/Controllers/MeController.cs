using AgroTrade.Application.Auth;
using AgroTrade.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

/// <summary>Authenticated user's own profile (plan §03 — Auth & Identity / /me).</summary>
[ApiController]
[Route("api/v1/me")]
[Authorize]
public class MeController(IAuthService authService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthUserDto>> Get(CancellationToken ct)
    {
        return Ok(await authService.GetCurrentUserAsync(User.GetUserId(), ct));
    }

    [HttpPut]
    [ProducesResponseType(typeof(AuthUserDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthUserDto>> Update(UpdateProfileRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            throw ApiException.BadRequest("INVALID_BODY", "A JSON request body is required.");
        }

        return Ok(await authService.UpdateProfileAsync(User.GetUserId(), request, ct));
    }
}