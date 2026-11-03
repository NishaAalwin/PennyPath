using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PennyPath.Application.Security;

namespace PennyPath.Api.Controllers;

/// <summary>The signed-in user's own security activity and privacy choices.</summary>
[ApiController]
[Authorize]
public class SecurityController(IAuditLog audit, IAiConsent aiConsent) : ControllerBase
{
    public record AiConsentRequest(bool Enabled);

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet("api/security/events")]
    public async Task<IActionResult> Events(CancellationToken ct) => Ok(await audit.RecentAsync(UserId, 50, ct));

    [HttpGet("api/privacy/ai")]
    public async Task<IActionResult> GetAiConsent(CancellationToken ct) => Ok(new { enabled = await aiConsent.IsEnabledAsync(UserId, ct) });

    [HttpPut("api/privacy/ai")]
    public async Task<IActionResult> SetAiConsent(AiConsentRequest request, CancellationToken ct)
    {
        if (!await aiConsent.SetAsync(UserId, request.Enabled, ct)) return NotFound();
        await audit.RecordAsync(UserId, request.Enabled ? AuditEventTypes.AiConsentGranted : AuditEventTypes.AiConsentRevoked, ClientIp, ct);
        return NoContent();
    }
}
