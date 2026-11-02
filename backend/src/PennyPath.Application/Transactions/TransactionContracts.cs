using PennyPath.Domain.Entities;

namespace PennyPath.Application.Transactions;

public record SmsImportRequest(IReadOnlyList<string> Messages);

public record ImportResult(int Imported, int Duplicates, IReadOnlyList<string> Unparsed);

public record TransactionDto(
    Guid Id, DateOnly Date, decimal Amount, TransactionType Type,
    string Merchant, string Category, string? Account, TransactionSource Source);

public interface ITransactionService
{
    Task<ImportResult> ImportSmsAsync(Guid userId, IReadOnlyList<string> messages, CancellationToken ct = default);

    /// <param name="month">Optional "yyyy-MM" filter.</param>
    Task<IReadOnlyList<TransactionDto>> ListAsync(Guid userId, string? month, CancellationToken ct = default);
}
