using PennyPath.Domain.Entities;

namespace PennyPath.Application.Summary;

public record SummaryLine(string Name, decimal Amount);

/// <param name="Income">Credits that are not refunds, transfers or duplicates.</param>
/// <param name="Expense">Debits minus refunds; never includes transfers or duplicates.</param>
/// <param name="Excluded">Rows in the month left out of the totals (possible duplicates and transfers); the list still shows them.</param>
public record MonthlySummary(
    string Month, decimal Income, decimal Expense, decimal Savings,
    IReadOnlyList<SummaryLine> ByCategory, IReadOnlyList<SummaryLine> ByMerchant,
    int Counted, int Excluded);

public record SummaryRow(
    TransactionType Type, decimal Amount, bool IsRefund, bool IsTransfer, bool IsDuplicate, string Category, string Merchant);

public static class SummaryCalculator
{
    public const string UnknownPlace = "Unknown";

    /// <summary>
    /// Rules from the brain (M9/M6): duplicates and transfers are left out; a refund reduces expense and is never income.
    /// </summary>
    public static MonthlySummary Compute(string month, IReadOnlyCollection<SummaryRow> rows)
    {
        var counted = rows.Where(r => !r.IsDuplicate && !r.IsTransfer).ToList();

        // Signed spend: expense adds, refund subtracts.
        var spend = counted
            .Where(r => r.Type == TransactionType.Expense || r.IsRefund)
            .Select(r => (Row: r, Signed: r.IsRefund ? -r.Amount : r.Amount))
            .ToList();

        var income = counted.Where(r => r.Type == TransactionType.Income && !r.IsRefund).Sum(r => r.Amount);
        var expense = spend.Sum(x => x.Signed);

        return new MonthlySummary(
            month, income, expense, income - expense,
            Group(spend, x => x.Row.Category),
            Group(spend, x => string.IsNullOrWhiteSpace(x.Row.Merchant) ? UnknownPlace : x.Row.Merchant.Trim()),
            counted.Count, rows.Count - counted.Count);
    }

    private static IReadOnlyList<SummaryLine> Group(
        IEnumerable<(SummaryRow Row, decimal Signed)> spend, Func<(SummaryRow Row, decimal Signed), string> key) =>
        spend.GroupBy(key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SummaryLine(g.Key, g.Sum(x => x.Signed)))
            .OrderByDescending(l => l.Amount).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
