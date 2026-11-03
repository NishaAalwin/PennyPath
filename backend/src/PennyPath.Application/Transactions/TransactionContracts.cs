using PennyPath.Domain.Entities;

namespace PennyPath.Application.Transactions;

public record SmsImportRequest(IReadOnlyList<string> Messages);

public record ImportResult(int Imported, int Duplicates, IReadOnlyList<string> Unparsed, int PossibleDuplicates = 0);

public record TransactionDto(
    Guid Id, DateOnly Date, decimal Amount, TransactionType Type,
    string Merchant, string Category, string? Account, TransactionSource Source,
    bool IsTransfer, bool IsRefund, Guid? RefundOfId, Guid? DuplicateOfId);

public record UnparsedDto(Guid Id, string Text, DateTime CreatedAt);

public record SetTransferRequest(bool IsTransfer);

public enum TransactionActionResult { Done, NotFound }

public record UpdateCategoryRequest(string? Category);

public enum CategoryUpdateResult { Updated, NotFound, InvalidCategory }

public interface ITransactionService
{
    Task<TransactionActionResult> SetTransferAsync(Guid userId, Guid transactionId, bool isTransfer, CancellationToken ct = default);

    /// <summary>The user says a flagged row is a genuine separate payment.</summary>
    Task<TransactionActionResult> ClearDuplicateAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    /// <summary>Deletes the row and clears any flags in other rows that pointed at it.</summary>
    Task<TransactionActionResult> DeleteAsync(Guid userId, Guid transactionId, CancellationToken ct = default);

    Task<IReadOnlyList<UnparsedDto>> ListUnparsedAsync(Guid userId, CancellationToken ct = default);
    Task<TransactionActionResult> DismissUnparsedAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Sets the user's own category. Returns NotFound for another user's or a missing transaction.</summary>
    Task<CategoryUpdateResult> UpdateCategoryAsync(Guid userId, Guid transactionId, string? category, CancellationToken ct = default);

    Task<ImportResult> ImportSmsAsync(Guid userId, IReadOnlyList<string> messages, CancellationToken ct = default);

    /// <param name="month">Optional "yyyy-MM" filter.</param>
    Task<IReadOnlyList<TransactionDto>> ListAsync(Guid userId, string? month, CancellationToken ct = default);
}
