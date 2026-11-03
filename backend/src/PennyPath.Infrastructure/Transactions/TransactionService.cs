using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Categorization;
using PennyPath.Application.Parsing;
using PennyPath.Application.Reconciliation;
using PennyPath.Application.Summary;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Transactions;

public partial class TransactionService(AppDbContext db, ITransactionParser parser, ICategorizer categorizer, TimeProvider clock) : ITransactionService
{
    public const int MaxMessages = 500;
    public const int MaxMessageLength = 2000;

    public async Task<ImportResult> ImportSmsAsync(Guid userId, IReadOnlyList<string> messages, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var unparsed = new List<string>();
        var candidates = new Dictionary<string, Transaction>(); // by hash: also dedupes within one batch

        foreach (var raw in messages.Take(MaxMessages))
        {
            var text = (raw ?? string.Empty).Trim();
            if (text.Length == 0) continue;
            if (text.Length > MaxMessageLength) text = text[..MaxMessageLength];

            var hash = Hash(text);
            var parsed = parser.Parse(text, today);
            if (parsed is null)
            {
                unparsed.Add(text.Length > 80 ? text[..80] + "..." : text);
                continue;
            }

            candidates.TryAdd(hash, new Transaction
            {
                UserId = userId,
                Date = parsed.Date,
                Amount = parsed.Amount,
                Type = parsed.Type,
                Merchant = parsed.Merchant ?? string.Empty,
                Category = categorizer.Categorize(parsed.Merchant, parsed.Type),
                IsRefund = parsed.IsRefund,
                Source = TransactionSource.Sms,
                RawHash = hash,
            });
        }

        var hashes = candidates.Keys.ToList();
        var existing = await db.Transactions
            .Where(t => t.UserId == userId && hashes.Contains(t.RawHash))
            .Select(t => t.RawHash).ToListAsync(ct);

        var fresh = candidates.Where(c => !existing.Contains(c.Key)).Select(c => c.Value).ToList();
        if (fresh.Count > 0) TransactionReconciler.Reconcile(fresh, await LoadRelatedAsync(userId, fresh, ct));
        db.Transactions.AddRange(fresh);

        await db.SaveChangesAsync(ct);

        var duplicates = messages.Count(m => !string.IsNullOrWhiteSpace(m)) - unparsed.Count - fresh.Count;
        return new ImportResult(fresh.Count, duplicates, unparsed, fresh.Count(t => t.DuplicateOfId is not null));
    }

    /// <summary>The user's stored rows that a new batch could duplicate, refund or pair with.</summary>
    private async Task<IReadOnlyList<Transaction>> LoadRelatedAsync(Guid userId, List<Transaction> fresh, CancellationToken ct)
    {
        var from = fresh.Min(t => t.Date).AddDays(-TransactionReconciler.RefundWindowDays);
        var to = fresh.Max(t => t.Date).AddDays(TransactionReconciler.RefundWindowDays);
        var amounts = fresh.Select(t => t.Amount).Distinct().ToList();

        return await db.Transactions
            .Where(t => t.UserId == userId && amounts.Contains(t.Amount) && t.Date >= from && t.Date <= to)
            .ToListAsync(ct);
    }

    public async Task<TransactionActionResult> SetTransferAsync(Guid userId, Guid transactionId, bool isTransfer, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, ct);
        if (tx is null) return TransactionActionResult.NotFound;

        if (isTransfer) TransactionReconciler.MarkTransfer(tx);
        else
        {
            tx.IsTransfer = false;
            if (!tx.CategoryIsManual && tx.Category == TransactionReconciler.TransferCategory)
                tx.Category = categorizer.Categorize(tx.Merchant, tx.Type);
        }

        await db.SaveChangesAsync(ct);
        return TransactionActionResult.Done;
    }

    public async Task<TransactionActionResult> ClearDuplicateAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, ct);
        if (tx is null) return TransactionActionResult.NotFound;

        tx.DuplicateOfId = null;
        await db.SaveChangesAsync(ct);
        return TransactionActionResult.Done;
    }

    public async Task<TransactionActionResult> DeleteAsync(Guid userId, Guid transactionId, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, ct);
        if (tx is null) return TransactionActionResult.NotFound;

        var pointing = await db.Transactions
            .Where(t => t.UserId == userId && (t.DuplicateOfId == transactionId || t.RefundOfId == transactionId))
            .ToListAsync(ct);
        foreach (var t in pointing)
        {
            if (t.DuplicateOfId == transactionId) t.DuplicateOfId = null;
            if (t.RefundOfId == transactionId) t.RefundOfId = null;
        }

        db.Transactions.Remove(tx);
        await db.SaveChangesAsync(ct);
        return TransactionActionResult.Done;
    }

    public async Task<CategoryUpdateResult> UpdateCategoryAsync(Guid userId, Guid transactionId, string? category, CancellationToken ct = default)
    {
        if (!Categories.IsValid(category)) return CategoryUpdateResult.InvalidCategory;

        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, ct);
        if (tx is null) return CategoryUpdateResult.NotFound;

        tx.Category = category!;
        tx.CategoryIsManual = true;
        await db.SaveChangesAsync(ct);
        return CategoryUpdateResult.Updated;
    }

    public async Task<IReadOnlyList<TransactionDto>> ListAsync(Guid userId, string? month, CancellationToken ct = default)
    {
        var q = db.Transactions.Where(t => t.UserId == userId);
        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                throw new ArgumentException("month must be formatted yyyy-MM", nameof(month));
            var end = start.AddMonths(1);
            q = q.Where(t => t.Date >= start && t.Date < end);
        }

        return await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt)
            .Select(t => new TransactionDto(t.Id, t.Date, t.Amount, t.Type, t.Merchant, t.Category, t.Source,
                t.IsTransfer, t.IsRefund, t.RefundOfId, t.DuplicateOfId))
            .ToListAsync(ct);
    }

    public async Task<MonthlySummary> SummaryAsync(Guid userId, string? month, CancellationToken ct = default)
    {
        var start = month is null
            ? new DateOnly(clock.GetUtcNow().Year, clock.GetUtcNow().Month, 1)
            : DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : throw new ArgumentException("month must be formatted yyyy-MM", nameof(month));
        var end = start.AddMonths(1);

        var rows = await db.Transactions
            .Where(t => t.UserId == userId && t.Date >= start && t.Date < end)
            .Select(t => new SummaryRow(t.Type, t.Amount, t.IsRefund, t.IsTransfer, t.DuplicateOfId != null, t.Category, t.Merchant))
            .ToListAsync(ct);

        return SummaryCalculator.Compute(start.ToString("yyyy-MM", CultureInfo.InvariantCulture), rows);
    }

    private static string Hash(string text)
    {
        var normalized = Whitespace().Replace(text, " ").Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
