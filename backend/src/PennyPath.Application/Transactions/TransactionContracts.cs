using PennyPath.Application.Summary;
using PennyPath.Domain.Entities;

namespace PennyPath.Application.Transactions;

public record SmsImportRequest(IReadOnlyList<string> Messages);

public record ImportResult(int Imported, int Duplicates, IReadOnlyList<string> Unparsed, int PossibleDuplicates = 0);

/// <param name="NotRecognisedSubjects">Shortened subjects of emails that could not be read; shown once, never stored.</param>
public record EmailImportResult(int Imported, int Duplicates, int PossibleDuplicates, IReadOnlyList<string> NotRecognisedSubjects);

public record TransactionDto(
    Guid Id, DateOnly Date, decimal Amount, TransactionType Type,
    string Merchant, string Category, TransactionSource Source,
    bool IsTransfer, bool IsRefund, Guid? RefundOfId, Guid? DuplicateOfId);

public record SetTransferRequest(bool IsTransfer);

/// <summary>Filters for the list. Every filter is optional; they combine with AND.</summary>
public record TransactionQuery(
    string? Month = null, DateOnly? From = null, DateOnly? To = null, string? Search = null,
    string? Category = null, TransactionType? Type = null, int Page = 1, int PageSize = 50);

public record PagedTransactions(IReadOnlyList<TransactionDto> Items, int Total, int Page, int PageSize);

/// <summary>Only the fields that are present are changed.</summary>
public record UpdateTransactionRequest(string? Merchant, decimal? Amount, DateOnly? Date, TransactionType? Type);

/// <param name="Found">False when the transaction does not exist or belongs to someone else.</param>
/// <param name="Error">A message for the user when the change was refused.</param>
public record EditResult(bool Found, string? Error, TransactionDto? Transaction);

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

    /// <summary>Sets the user's own category. Returns NotFound for another user's or a missing transaction.</summary>
    Task<CategoryUpdateResult> UpdateCategoryAsync(Guid userId, Guid transactionId, string? category, CancellationToken ct = default);

    Task<ImportResult> ImportSmsAsync(Guid userId, IReadOnlyList<string> messages, CancellationToken ct = default);

    /// <summary>Parses and saves alert emails. The message id (not the text) is the repeat-proof key; no email text is stored.</summary>
    Task<EmailImportResult> ImportEmailsAsync(Guid userId, IReadOnlyList<Gmail.EmailItem> items, CancellationToken ct = default);

    /// <summary>Income, expense, savings and totals by place and category for one month (default: current month).</summary>
    Task<MonthlySummary> SummaryAsync(Guid userId, string? month, CancellationToken ct = default);

    /// <summary>
    /// Income, expense and savings for each of the last <paramref name="months"/> months (oldest first, empty months
    /// included), ending at <paramref name="to"/> ("yyyy-MM", default current month).
    /// </summary>
    Task<IReadOnlyList<TrendMonth>> TrendAsync(Guid userId, int months, string? to, CancellationToken ct = default);

    /// <summary>Search, filter and page through the user's transactions (newest first).</summary>
    Task<PagedTransactions> QueryAsync(Guid userId, TransactionQuery query, CancellationToken ct = default);

    /// <summary>Edit merchant, amount, date or direction of the user's own transaction.</summary>
    Task<EditResult> UpdateAsync(Guid userId, Guid transactionId, UpdateTransactionRequest request, CancellationToken ct = default);

    /// <summary>All rows for a month, unpaged (for exports and tests; the API list endpoint is paged).</summary>
    /// <param name="month">Optional "yyyy-MM" filter.</param>
    Task<IReadOnlyList<TransactionDto>> ListAsync(Guid userId, string? month, CancellationToken ct = default);
}
