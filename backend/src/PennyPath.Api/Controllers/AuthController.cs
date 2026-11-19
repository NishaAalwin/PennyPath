using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using PennyPath.Api.Security;
using PennyPath.Application.Account;
using PennyPath.Application.Auth;
using PennyPath.Application.Security;

namespace PennyPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService auth, IAccountService account, IAuditLog audit, SessionCookieSettings cookie) : ControllerBase
{
    public record TokenRequest(string? Token);
    public record EmailRequest(string? Email);
    public record ResetRequest(string? Token, string? NewPassword);

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var r = await auth.RegisterAsync(request, ct);
        if (r.Succeeded)
        {
            await audit.RecordAsync(r.UserId, AuditEventTypes.Registered, ClientIp, ct);
            await account.SendVerificationAsync(r.UserId!.Value, ct); // queued; does nothing when verification is switched off
        }
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

    [Authorize(Policy = "Authenticated")]
    [DisableRateLimiting]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (SessionId is { } sid) await auth.LogoutAsync(UserId, sid, ct);
        await audit.RecordAsync(UserId, AuditEventTypes.LoggedOut, ClientIp, ct);
        Response.Cookies.Delete(cookie.Name, cookie.Options());
        return NoContent();
    }

    [Authorize(Policy = "Authenticated")]
    [DisableRateLimiting]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await auth.LogoutAllAsync(UserId, ct);
        await audit.RecordAsync(UserId, AuditEventTypes.LoggedOutEverywhere, ClientIp, ct);
        Response.Cookies.Delete(cookie.Name, cookie.Options());
        return NoContent();
    }

    // Open to accounts that have not verified their email yet: the app needs it to show the "verify your email" screen.
    [Authorize(Policy = "Authenticated")]
    [DisableRateLimiting]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        email = User.FindFirstValue(ClaimTypes.Email),
        emailVerified = User.FindFirstValue("email_verified") == "true",
    });

    [Authorize(Policy = "Authenticated")]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(CancellationToken ct)
    {
        await account.SendVerificationAsync(UserId, ct);
        return Accepted(new { message = "If your email is not verified yet, a new link is on its way." });
    }

    [AllowAnonymous]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(TokenRequest request, CancellationToken ct) =>
        await account.VerifyEmailAsync(request.Token, ClientIp, ct)
            ? NoContent()
            : BadRequest(new { error = "This link is invalid or has expired. Sign in and ask for a new one." });

    /// <summary>Always answers the same way, whether or not the address has an account.</summary>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken ct)
    {
        await account.RequestPasswordResetAsync(request.Email, ClientIp, ct);
        return Accepted(new { message = "If that address has an account, a reset link is on its way." });
    }

    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetRequest request, CancellationToken ct)
    {
        var r = await account.ResetPasswordAsync(request.Token, request.NewPassword, ClientIp, ct);
        return r.Outcome == ResetOutcome.Done ? NoContent() : BadRequest(new { error = r.Message });
    }

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
                AuthError.MfaRequired => Ok(new { mfaRequired = true }), // the password was right; ask for the code, no session yet
                _ => Unauthorized(new { error = r.Message }),
            };

        // The token goes into the HttpOnly cookie only; it is never put in the response body.
        var ok = r.Response!;
        Response.Cookies.Append(cookie.Name, ok.Token, cookie.Options(new DateTimeOffset(DateTime.SpecifyKind(ok.ExpiresAt, DateTimeKind.Utc))));
        var body = new { expiresAt = ok.ExpiresAt, email = ok.Email };
        return created ? StatusCode(StatusCodes.Status201Created, body) : Ok(body);
    }
}
