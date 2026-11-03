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

    [HttpGet("api/categories")]
    public IActionResult Categories() => Ok(PennyPath.Application.Categorization.Categories.All);

    [HttpPut("api/transactions/{id:guid}/category")]
    public async Task<IActionResult> SetCategory(Guid id, UpdateCategoryRequest request, CancellationToken ct) =>
        await transactions.UpdateCategoryAsync(UserId, id, request.Category, ct) switch
        {
            CategoryUpdateResult.Updated => NoContent(),
            CategoryUpdateResult.InvalidCategory => BadRequest(new { error = "Unknown category." }),
            _ => NotFound(),
        };

    [HttpPut("api/transactions/{id:guid}/transfer")]
    public async Task<IActionResult> SetTransfer(Guid id, SetTransferRequest request, CancellationToken ct) =>
        Result(await transactions.SetTransferAsync(UserId, id, request.IsTransfer, ct));

    [HttpPost("api/transactions/{id:guid}/not-duplicate")]
    public async Task<IActionResult> NotDuplicate(Guid id, CancellationToken ct) =>
        Result(await transactions.ClearDuplicateAsync(UserId, id, ct));

    [HttpDelete("api/transactions/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) =>
        Result(await transactions.DeleteAsync(UserId, id, ct));

    [HttpGet("api/review/unparsed")]
    public async Task<IActionResult> Unparsed(CancellationToken ct) =>
        Ok(await transactions.ListUnparsedAsync(UserId, ct));

    [HttpDelete("api/review/unparsed/{id:guid}")]
    public async Task<IActionResult> DismissUnparsed(Guid id, CancellationToken ct) =>
        Result(await transactions.DismissUnparsedAsync(UserId, id, ct));

    private IActionResult Result(TransactionActionResult r) =>
        r == TransactionActionResult.Done ? NoContent() : NotFound();

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
