using PennyPath.Domain.Entities;

namespace PennyPath.Application.Transactions;

public record SmsImportRequest(IReadOnlyList<string> Messages);

public record ImportResult(int Imported, int Duplicates, IReadOnlyList<string> Unparsed);

public record TransactionDto(
    Guid Id, DateOnly Date, decimal Amount, TransactionType Type,
    string Merchant, string Category, string? Account, TransactionSource Source);

public record UpdateCategoryRequest(string? Category);

public enum CategoryUpdateResult { Updated, NotFound, InvalidCategory }

public interface ITransactionService
{
    /// <summary>Sets the user's own category. Returns NotFound for another user's or a missing transaction.</summary>
    Task<CategoryUpdateResult> UpdateCategoryAsync(Guid userId, Guid transactionId, string? category, CancellationToken ct = default);

    Task<ImportResult> ImportSmsAsync(Guid userId, IReadOnlyList<string> messages, CancellationToken ct = default);

    /// <param name="month">Optional "yyyy-MM" filter.</param>
    Task<IReadOnlyList<TransactionDto>> ListAsync(Guid userId, string? month, CancellationToken ct = default);
}
