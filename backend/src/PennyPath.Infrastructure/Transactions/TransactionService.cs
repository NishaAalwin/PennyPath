using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Parsing;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Transactions;

public partial class TransactionService(AppDbContext db, ITransactionParser parser, TimeProvider clock) : ITransactionService
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

            var parsed = parser.Parse(text, today);
            if (parsed is null)
            {
                unparsed.Add(text.Length > 80 ? text[..80] + "..." : text);
                continue;
            }

            var hash = Hash(text);
            candidates.TryAdd(hash, new Transaction
            {
                UserId = userId,
                Date = parsed.Date,
                Amount = parsed.Amount,
                Type = parsed.Type,
                Merchant = parsed.Merchant ?? string.Empty,
                Account = parsed.Account,
                Source = TransactionSource.Sms,
                RawHash = hash,
            });
        }

        var hashes = candidates.Keys.ToList();
        var existing = await db.Transactions
            .Where(t => t.UserId == userId && hashes.Contains(t.RawHash))
            .Select(t => t.RawHash).ToListAsync(ct);

        var fresh = candidates.Where(c => !existing.Contains(c.Key)).Select(c => c.Value).ToList();
        db.Transactions.AddRange(fresh);
        await db.SaveChangesAsync(ct);

        var duplicates = messages.Count(m => !string.IsNullOrWhiteSpace(m)) - unparsed.Count - fresh.Count;
        return new ImportResult(fresh.Count, duplicates, unparsed);
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
            .Select(t => new TransactionDto(t.Id, t.Date, t.Amount, t.Type, t.Merchant, t.Category, t.Account, t.Source))
            .ToListAsync(ct);
    }

    private static string Hash(string text)
    {
        var normalized = Whitespace().Replace(text, " ").Trim().ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
