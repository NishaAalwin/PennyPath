using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PennyPath.Application.Gmail;

namespace PennyPath.Api.Controllers;

[ApiController]
[EnableRateLimiting("gmail")]
public class GmailController(IGmailService gmail) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [Authorize]
    [HttpGet("api/gmail")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await gmail.StatusAsync(UserId, ct));

    /// <summary>Returns the Google consent address; the browser then navigates there.</summary>
    [Authorize]
    [HttpPost("api/gmail/connect")]
    public async Task<IActionResult> Connect(CancellationToken ct)
    {
        var url = await gmail.StartConnectAsync(UserId, ct);
        return url is null
            ? Conflict(new { error = "Gmail is not set up on this server yet (see docs/operations.md)." })
            : Ok(new { url });
    }

    /// <summary>
    /// Where Google sends the browser back. The login cookie is SameSite=Strict, so it is not sent on this cross-site
    /// navigation; the user is identified by the single-use, short-lived state created in <see cref="Connect"/> instead.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/gmail/callback")]
    public async Task<IActionResult> Callback(string? state, string? code, string? error, CancellationToken ct) =>
        LocalRedirect(await gmail.CompleteConnectAsync(state, code, error, ClientIp, ct) ? "/?gmail=connected" : "/?gmail=failed");

    [Authorize]
    [HttpPost("api/gmail/sync")]
    public async Task<IActionResult> Sync(CancellationToken ct)
    {
        var outcome = await gmail.SyncAsync(UserId, ClientIp, ct);
        return outcome.Status switch
        {
            GmailSyncStatus.Ok => Ok(outcome.Result),
            GmailSyncStatus.NotConfigured => Conflict(new { error = "Gmail is not set up on this server yet (see docs/operations.md)." }),
            GmailSyncStatus.NotConnected => Conflict(new { error = "Connect Gmail first." }),
            GmailSyncStatus.NeedsReconnect => Conflict(new { error = "Google no longer accepts the saved connection (it was revoked or expired). Connect Gmail again.", reconnect = true }),
            GmailSyncStatus.Busy => StatusCode(StatusCodes.Status429TooManyRequests, new { error = "A sync is already running." }),
            _ => StatusCode(StatusCodes.Status502BadGateway, new { error = "Could not reach Gmail. Try again in a moment." }),
        };
    }

    [Authorize]
    [HttpDelete("api/gmail")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        await gmail.DisconnectAsync(UserId, ClientIp, ct);
        return NoContent();
    }
}
