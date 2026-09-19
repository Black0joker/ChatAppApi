using ChatApp.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    public sealed record RegisterBody(string Username, string DisplayName, string Email, string Password);
    public sealed record LoginBody(string UsernameOrEmail, string Password);
    public sealed record RefreshBody(string RefreshToken);
    public sealed record LogoutBody(string RefreshToken);

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterBody body, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(new RegisterRequest(body.Username, body.DisplayName, body.Email, body.Password), ct);
        return CreatedAtAction(nameof(Register), result);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginBody body, CancellationToken ct)
    {
        var result = await auth.LoginAsync(new LoginRequest(body.UsernameOrEmail, body.Password), ct);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh([FromBody] RefreshBody body, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(new RefreshRequest(body.RefreshToken), ct);
        return Ok(result);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutBody body, CancellationToken ct)
    {
        await auth.LogoutAsync(body.RefreshToken, ct);
        return NoContent();
    }
}
