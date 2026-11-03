using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using PennyPath.Api.Security;
using PennyPath.Application.Auth;
using PennyPath.Application.Security;

namespace PennyPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService auth, IAuditLog audit, SessionCookieSettings cookie) : ControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var r = await auth.RegisterAsync(request, ct);
        if (r.Succeeded) await audit.RecordAsync(r.UserId, AuditEventTypes.Registered, ClientIp, ct);
        return ToResponse(r, created: true);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var r = await auth.LoginAsync(request, ct);
        var type = r.Error switch
        {
            AuthError.None => AuditEventTypes.LoginSucceeded,
            AuthError.LockedOut => AuditEventTypes.LoginBlockedByLockout,
            AuthError.InvalidCredentials => AuditEventTypes.LoginFailed,
            _ => null,
        };
        if (type is not null) await audit.RecordAsync(r.UserId, type, ClientIp, ct);
        return ToResponse(r, created: false);
    }

    [Authorize]
    [DisableRateLimiting]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (SessionId is { } sid) await auth.LogoutAsync(UserId, sid, ct);
        await audit.RecordAsync(UserId, AuditEventTypes.LoggedOut, ClientIp, ct);
        Response.Cookies.Delete(cookie.Name, cookie.Options());
        return NoContent();
    }

    [Authorize]
    [DisableRateLimiting]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await auth.LogoutAllAsync(UserId, ct);
        await audit.RecordAsync(UserId, AuditEventTypes.LoggedOutEverywhere, ClientIp, ct);
        Response.Cookies.Delete(cookie.Name, cookie.Options());
        return NoContent();
    }

    [Authorize]
    [DisableRateLimiting]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        email = User.FindFirstValue(ClaimTypes.Email),
    });

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private Guid? SessionId => Guid.TryParse(User.FindFirstValue(JwtRegisteredClaimNames.Jti), out var id) ? id : null;

    private IActionResult ToResponse(AuthResult r, bool created)
    {
        if (!r.Succeeded)
            return r.Error switch
            {
                AuthError.InvalidInput => BadRequest(new { error = r.Message }),
                AuthError.EmailTaken => Conflict(new { error = r.Message }),
                AuthError.LockedOut => StatusCode(StatusCodes.Status429TooManyRequests, new { error = r.Message }),
                _ => Unauthorized(new { error = r.Message }),
            };

        // The token goes into the HttpOnly cookie only; it is never put in the response body.
        var ok = r.Response!;
        Response.Cookies.Append(cookie.Name, ok.Token, cookie.Options(new DateTimeOffset(DateTime.SpecifyKind(ok.ExpiresAt, DateTimeKind.Utc))));
        var body = new { expiresAt = ok.ExpiresAt, email = ok.Email };
        return created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }
}
