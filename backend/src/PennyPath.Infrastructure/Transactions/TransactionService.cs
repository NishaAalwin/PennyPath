using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Categorization;
using PennyPath.Application.Gmail;
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

        var fresh = await SaveNewAsync(userId, candidates, ct);

        var duplicates = messages.Count(m => !string.IsNullOrWhiteSpace(m)) - unparsed.Count - fresh.Count;
        return new ImportResult(fresh.Count, duplicates, unparsed, fresh.Count(t => t.DuplicateOfId is not null));
    }

    /// <summary>The key that makes a Gmail sync repeatable: derived from the message id, so no email text is needed to recognise it.</summary>
    public static string EmailKey(string messageId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("gmail:" + messageId)));

    public async Task<EmailImportResult> ImportEmailsAsync(Guid userId, IReadOnlyList<EmailItem> items, CancellationToken ct = default)
    {
        var unrecognised = new List<string>();
        var candidates = new Dictionary<string, Transaction>();

        foreach (var item in items.Take(MaxMessages))
        {
            var parsed = parser.Parse(EmailTextExtractor.BuildParsableText(item.Subject, item.Body), item.ReceivedOn);
            if (parsed is null)
            {
                var subject = Whitespace().Replace(item.Subject ?? string.Empty, " ").Trim();
                unrecognised.Add(subject.Length == 0 ? "(no subject)" : subject.Length > 80 ? subject[..80] + "..." : subject);
                continue;
            }

            var key = EmailKey(item.Id);
            candidates.TryAdd(key, new Transaction
            {
                UserId = userId,
                Date = parsed.Date,
                Amount = parsed.Amount,
                Type = parsed.Type,
                Merchant = parsed.Merchant ?? string.Empty,
                Category = categorizer.Categorize(parsed.Merchant, parsed.Type),
                IsRefund = parsed.IsRefund,
                Source = TransactionSource.Email,
                RawHash = key,
            });
        }

        var fresh = await SaveNewAsync(userId, candidates, ct);
        return new EmailImportResult(fresh.Count, candidates.Count - fresh.Count, fresh.Count(t => t.DuplicateOfId is not null), unrecognised);
    }

    /// <summary>Saves the candidates that are not already stored (by key), flagging duplicates, refunds and the like.</summary>
    private async Task<List<Transaction>> SaveNewAsync(Guid userId, Dictionary<string, Transaction> candidates, CancellationToken ct)
    {
        var hashes = candidates.Keys.ToList();
        var existing = await db.Transactions
            .Where(t => t.UserId == userId && hashes.Contains(t.RawHash))
            .Select(t => t.RawHash).ToListAsync(ct);

        var fresh = candidates.Where(c => !existing.Contains(c.Key)).Select(c => c.Value).ToList();
        if (fresh.Count > 0) TransactionReconciler.Reconcile(fresh, await LoadRelatedAsync(userId, fresh, ct));
        db.Transactions.AddRange(fresh);

        await db.SaveChangesAsync(ct);
        return fresh;
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

    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 100;
    public const int MaxMerchantLength = 200;
    public const decimal MaxAmount = 999_999_999.99m;
    public static readonly DateOnly MinDate = new(2000, 1, 1);

    public async Task<PagedTransactions> QueryAsync(Guid userId, TransactionQuery query, CancellationToken ct = default)
    {
        if (query.Page < 1) throw new ArgumentException("page must be 1 or more", nameof(query));
        if (query.PageSize < 1 || query.PageSize > MaxPageSize) throw new ArgumentException($"pageSize must be 1-{MaxPageSize}", nameof(query));

        var q = db.Transactions.Where(t => t.UserId == userId);

        if (query.Month is not null)
        {
            if (!DateOnly.TryParseExact(query.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
                throw new ArgumentException("month must be formatted yyyy-MM", nameof(query));
            var end = start.AddMonths(1);
            q = q.Where(t => t.Date >= start && t.Date < end);
        }
        if (query.From is { } from) q = q.Where(t => t.Date >= from);
        if (query.To is { } to) q = q.Where(t => t.Date <= to);
        if (query.Type is { } type) q = q.Where(t => t.Type == type);
        if (query.Category is not null)
        {
            if (!Categories.IsValid(query.Category)) throw new ArgumentException("unknown category", nameof(query));
            var category = query.Category;
            q = q.Where(t => t.Category == category);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            if (term.Length > MaxSearchLength) throw new ArgumentException("search is too long", nameof(query));
            var lower = term.ToLowerInvariant();
            q = decimal.TryParse(term, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                ? q.Where(t => t.Merchant.ToLower().Contains(lower) || t.Amount == amount)
                : q.Where(t => t.Merchant.ToLower().Contains(lower));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(t => t.Date).ThenByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(t => new TransactionDto(t.Id, t.Date, t.Amount, t.Type, t.Merchant, t.Category, t.Source,
                t.IsTransfer, t.IsRefund, t.RefundOfId, t.DuplicateOfId))
            .ToListAsync(ct);
        return new PagedTransactions(items, total, query.Page, query.PageSize);
    }

    public async Task<EditResult> UpdateAsync(Guid userId, Guid transactionId, UpdateTransactionRequest request, CancellationToken ct = default)
    {
        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && t.UserId == userId, ct);
        if (tx is null) return new EditResult(false, null, null);

        if (request.Merchant is null && request.Amount is null && request.Date is null && request.Type is null)
            return Refused("Nothing to change.");

        var merchant = request.Merchant?.Trim();
        if (merchant is not null && (merchant.Length > MaxMerchantLength || merchant.Any(char.IsControl)))
            return Refused($"Where it was spent must be at most {MaxMerchantLength} characters, without control characters.");
        if (request.Amount is { } a && (a <= 0 || a > MaxAmount || decimal.Round(a, 2) != a))
            return Refused("Amount must be more than 0, at most 999,999,999.99, with at most 2 decimals.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (request.Date is { } date && (date < MinDate || date > today.AddDays(1)))
            return Refused("Date must be between 2000-01-01 and tomorrow.");
        if (request.Type is { } type && !Enum.IsDefined(type))
            return Refused("Type must be Income or Expense.");

        var changed =
            (merchant is not null && merchant != tx.Merchant) || (request.Amount is { } na && na != tx.Amount)
            || (request.Date is { } nd && nd != tx.Date) || (request.Type is { } nt && nt != tx.Type);

        if (merchant is not null) tx.Merchant = merchant;
        if (request.Amount is { } newAmount) tx.Amount = newAmount;
        if (request.Date is { } newDate) tx.Date = newDate;
        if (request.Type is { } newType)
        {
            tx.Type = newType;
            if (newType == TransactionType.Expense) tx.IsRefund = false;
        }

        if (changed)
        {
            if (!tx.CategoryIsManual && !tx.IsTransfer) tx.Category = categorizer.Categorize(tx.Merchant, tx.Type);

            // The user has corrected this row, so earlier guesses about it no longer hold.
            tx.DuplicateOfId = null;
            tx.RefundOfId = null;
            var pointing = await db.Transactions
                .Where(t => t.UserId == userId && (t.DuplicateOfId == transactionId || t.RefundOfId == transactionId))
                .ToListAsync(ct);
            foreach (var t in pointing)
            {
                if (t.DuplicateOfId == transactionId) t.DuplicateOfId = null;
                if (t.RefundOfId == transactionId) t.RefundOfId = null;
            }
        }

        await db.SaveChangesAsync(ct);
        return new EditResult(true, null, new TransactionDto(tx.Id, tx.Date, tx.Amount, tx.Type, tx.Merchant, tx.Category,
            tx.Source, tx.IsTransfer, tx.IsRefund, tx.RefundOfId, tx.DuplicateOfId));

        static EditResult Refused(string message) => new(true, message, null);
    }

    public const int MinTrendMonths = 2;
    public const int MaxTrendMonths = 12;

    public async Task<IReadOnlyList<TrendMonth>> TrendAsync(Guid userId, int months, string? to, CancellationToken ct = default)
    {
        if (months < MinTrendMonths || months > MaxTrendMonths)
            throw new ArgumentOutOfRangeException(nameof(months), $"months must be between {MinTrendMonths} and {MaxTrendMonths}");

        var last = to is null
            ? new DateOnly(clock.GetUtcNow().Year, clock.GetUtcNow().Month, 1)
            : DateOnly.TryParseExact(to + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d
                : throw new ArgumentException("to must be formatted yyyy-MM", nameof(to));
        var first = last.AddMonths(-(months - 1));
        var end = last.AddMonths(1);

        var rows = await db.Transactions
            .Where(t => t.UserId == userId && t.Date >= first && t.Date < end)
            .Select(t => new { t.Date, Row = new SummaryRow(t.Type, t.Amount, t.IsRefund, t.IsTransfer, t.DuplicateOfId != null, t.Category, t.Merchant) })
            .ToListAsync(ct);

        return Enumerable.Range(0, months).Select(i =>
        {
            var m = first.AddMonths(i);
            var s = SummaryCalculator.Compute(
                m.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                rows.Where(r => r.Date.Year == m.Year && r.Date.Month == m.Month).Select(r => r.Row).ToList());
            return new TrendMonth(s.Month, s.Income, s.Expense, s.Savings);
        }).ToList();
    }

    private static string Hash(string text)
    {
        var normalized = Whitespace().Replace(text, " ").Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
