using Grimoire.Library.Models;
using Grimoire.Server.Auth;
using Grimoire.Server.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Server.Controllers;

/// <summary>
/// Registration, login and token refresh. Passwords are handled by ASP.NET Identity;
/// sessions are stateless JWTs.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<GrimoireUser> _users;
    private readonly TokenService _tokens;

    public AuthController(UserManager<GrimoireUser> users, TokenService tokens)
    {
        _users = users;
        _tokens = tokens;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        GrimoireUser? existing = await _users.FindByEmailAsync(request.Email);

        if (existing is not null)
        {
            return Conflict(new { message = "An account with that email already exists." });
        }

        GrimoireUser user = new()
        {
            UserName = request.Email,
            Email = request.Email,
        };

        IdentityResult result = await _users.CreateAsync(user, request.Password);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });
        }

        return Ok(ToResponse(_tokens.CreatePair(user)));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        GrimoireUser? user = await _users.FindByEmailAsync(request.Email);

        if (user is null || !await _users.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(ToResponse(_tokens.CreatePair(user)));
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest request)
    {
        Guid? userId = _tokens.ReadRefreshTokenUserId(request.RefreshToken);

        if (userId is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        GrimoireUser? user = await _users.FindByIdAsync(userId.Value.ToString());

        if (user is null)
        {
            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        return Ok(ToResponse(_tokens.CreatePair(user)));
    }

    private static AuthResponse ToResponse(TokenPair pair)
    {
        return new AuthResponse(pair.AccessToken, pair.RefreshToken, pair.AccessTokenExpiresAt);
    }
}
