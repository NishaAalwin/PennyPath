using PennyPath.Domain.Entities;

namespace PennyPath.Application.Reconciliation;

/// <summary>
/// Flags duplicates and links refunds among freshly imported rows and the user's existing rows, using only the
/// stored fields (date, amount, direction, merchant). It only sets flags (nothing is deleted), so every
/// decision can be undone by the user. Own-account transfers are marked by the user: telling them apart
/// automatically needs account numbers, which are deliberately not read.
/// </summary>
public static class TransactionReconciler
{
    public const int RefundWindowDays = 60;
    public const string TransferCategory = "Transfers";

    /// <param name="fresh">Newly imported rows, in import order.</param>
    /// <param name="existing">The same user's stored rows that may relate to them (tracked entities are updated in place).</param>
    public static void Reconcile(IReadOnlyList<Transaction> fresh, IReadOnlyList<Transaction> existing)
    {
        var pool = new List<Transaction>(existing);

        foreach (var t in fresh)
        {
            var original = pool.FirstOrDefault(o => o.DuplicateOfId is null && IsDuplicate(t, o));
            if (original is not null) t.DuplicateOfId = original.Id;
            else if (t.IsRefund) LinkRefund(t, pool);
            pool.Add(t);
        }
    }

    public static void MarkTransfer(Transaction t)
    {
        t.IsTransfer = true;
        if (!t.CategoryIsManual) t.Category = TransferCategory;
    }

    /// <summary>Same direction, amount, day and a known, identical merchant: probably two messages about one payment.</summary>
    private static bool IsDuplicate(Transaction a, Transaction b) =>
        a.Id != b.Id && a.Type == b.Type && a.Amount == b.Amount && a.Date == b.Date
        && !string.IsNullOrWhiteSpace(a.Merchant)
        && string.Equals(a.Merchant, b.Merchant, StringComparison.OrdinalIgnoreCase);

    private static void LinkRefund(Transaction refund, List<Transaction> pool)
    {
        if (string.IsNullOrWhiteSpace(refund.Merchant)) return;

        var expense = pool
            .Where(e => e.Type == TransactionType.Expense && !e.IsTransfer && !e.IsRefund && e.DuplicateOfId is null
                        && e.Amount == refund.Amount
                        && string.Equals(e.Merchant, refund.Merchant, StringComparison.OrdinalIgnoreCase)
                        && e.Date <= refund.Date && refund.Date.DayNumber - e.Date.DayNumber <= RefundWindowDays
                        && !pool.Any(r => r.RefundOfId == e.Id))
            .OrderByDescending(e => e.Date)
            .FirstOrDefault();
        if (expense is null) return;

        refund.RefundOfId = expense.Id;
        if (!refund.CategoryIsManual) refund.Category = expense.Category;
    }
}
