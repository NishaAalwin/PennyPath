using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PennyPath.Application.Transactions;

namespace PennyPath.Api.Controllers;

[ApiController]
[Authorize]
public class TransactionsController(ITransactionService transactions) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("api/sms/import")]
    public async Task<IActionResult> ImportSms(SmsImportRequest request, CancellationToken ct)
    {
        if (request.Messages is null || request.Messages.Count == 0)
            return BadRequest(new { error = "Provide at least one message." });
        if (request.Messages.Count > 500)
            return BadRequest(new { error = "Import at most 500 messages at a time." });

        return Ok(await transactions.ImportSmsAsync(UserId, request.Messages, ct));
    }

    [HttpGet("api/transactions")]
    public async Task<IActionResult> List([FromQuery] string? month, CancellationToken ct)
    {
        try
        {
            return Ok(await transactions.ListAsync(UserId, month, ct));
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "month must be formatted yyyy-MM." });
        }
    }
}
