using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PennyPath.Application.Auth;

namespace PennyPath.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) =>
        ToResponse(await auth.RegisterAsync(request, ct), created: true);

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct) =>
        ToResponse(await auth.LoginAsync(request, ct), created: false);

    [Authorize]
    [DisableRateLimiting]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new
    {
        id = User.FindFirstValue(ClaimTypes.NameIdentifier),
        email = User.FindFirstValue(ClaimTypes.Email),
    });

    private IActionResult ToResponse(AuthResult r, bool created) => r.Error switch
    {
        AuthError.None => created ? StatusCode(StatusCodes.Status201Created, r.Response) : Ok(r.Response),
        AuthError.InvalidInput => BadRequest(new { error = r.Message }),
        AuthError.EmailTaken => Conflict(new { error = r.Message }),
        AuthError.LockedOut => StatusCode(StatusCodes.Status429TooManyRequests, new { error = r.Message }),
        _ => Unauthorized(new { error = r.Message }),
    };
}
