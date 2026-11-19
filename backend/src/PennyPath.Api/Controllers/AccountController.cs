using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PennyPath.Api.Security;
using PennyPath.Application.Account;

namespace PennyPath.Api.Controllers;

/// <summary>The signed-in user's own account: details, export, two-factor login and deletion.</summary>
[ApiController]
[Authorize]
[EnableRateLimiting("account")]
[Route("api/account")]
public class AccountController(IAccountService account, SessionCookieSettings cookie) : ControllerBase
{
    public record DeleteRequest(string? Password, string? Code);
    public record CodeRequest(string? Code);
    public record DisableMfaRequest(string? Password, string? Code);

    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet]
    public async Task<IActionResult> Info(CancellationToken ct) => await account.InfoAsync(UserId, ct) is { } info ? Ok(info) : NotFound();

    /// <summary>Everything stored about the user, as a JSON download. No password hash, token or other secret is in it.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var export = await account.ExportAsync(UserId, ClientIp, ct);
        if (export is null) return NotFound();
        return File(JsonSerializer.SerializeToUtf8Bytes(export, ExportJson), "application/json", "pennypath-export.json");
    }

    [HttpPost("delete")]
    public async Task<IActionResult> Delete(DeleteRequest request, CancellationToken ct)
    {
        switch (await account.DeleteAccountAsync(UserId, request.Password, request.Code, ClientIp, ct))
        {
            case DeleteOutcome.Deleted:
                Response.Cookies.Delete(cookie.Name, cookie.Options());
                return NoContent();
            case DeleteOutcome.MfaRequired:
                return Conflict(new { error = "Enter the code from your authenticator app to confirm.", mfaRequired = true });
            case DeleteOutcome.LockedOut:
                return StatusCode(StatusCodes.Status429TooManyRequests, new { error = "Too many failed attempts. Try again later." });
            default:
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "The password or code is not right." });
        }
    }

    [HttpPost("mfa/setup")]
    public async Task<IActionResult> MfaSetup(CancellationToken ct) =>
        await account.BeginMfaSetupAsync(UserId, ct) is { } setup ? Ok(setup) : Conflict(new { error = "Two-factor login is already on." });

    [HttpPost("mfa/enable")]
    public async Task<IActionResult> MfaEnable(CodeRequest request, CancellationToken ct)
    {
        var r = await account.EnableMfaAsync(UserId, request.Code, ClientIp, ct);
        return r.Enabled ? Ok(new { recoveryCodes = r.RecoveryCodes }) : BadRequest(new { error = "That code is not right. Check the app and try again." });
    }

    [HttpPost("mfa/disable")]
    public async Task<IActionResult> MfaDisable(DisableMfaRequest request, CancellationToken ct) =>
        await account.DisableMfaAsync(UserId, request.Password, request.Code, ClientIp, ct)
            ? NoContent()
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "The password or code is not right." });
}
