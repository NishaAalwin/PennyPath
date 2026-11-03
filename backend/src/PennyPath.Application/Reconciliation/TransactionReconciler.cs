using PennyPath.Application.Categorization;
using PennyPath.Domain.Entities;

namespace PennyPath.Application.Reconciliation;

/// <summary>
/// Flags duplicates, links refunds and pairs own-account transfers among freshly imported rows and the
/// user's existing rows. It only sets flags (nothing is deleted), so every decision can be undone by the user.
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

        foreach (var t in fresh.Where(t => t.DuplicateOfId is null && !t.IsRefund && !t.IsTransfer))
        {
            var partner = UniquePartner(t, pool);
            if (partner is null || UniquePartner(partner, pool) != t) continue;
            MarkTransfer(t);
            MarkTransfer(partner);
        }
    }

    public static void MarkTransfer(Transaction t)
    {
        t.IsTransfer = true;
        if (!t.CategoryIsManual) t.Category = TransferCategory;
    }

    private static bool IsDuplicate(Transaction a, Transaction b)
    {
        if (a.Id == b.Id || a.Type != b.Type || a.Amount != b.Amount) return false;

        // Two different reference numbers mean two real payments; the same number means the same payment.
        if (!string.IsNullOrEmpty(a.Reference) && !string.IsNullOrEmpty(b.Reference))
            return string.Equals(a.Reference, b.Reference, StringComparison.OrdinalIgnoreCase);

        return a.Date == b.Date && AccountsCompatible(a, b) && MerchantsCompatible(a, b);
    }

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

    private static Transaction? UniquePartner(Transaction t, List<Transaction> pool)
    {
        var candidates = pool.Where(o => IsTransferPartner(t, o)).Take(2).ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static bool IsTransferPartner(Transaction a, Transaction b) =>
        a.Id != b.Id && a.Type != b.Type && a.Amount == b.Amount
        && Math.Abs(a.Date.DayNumber - b.Date.DayNumber) <= 1
        && !string.IsNullOrEmpty(a.Account) && !string.IsNullOrEmpty(b.Account) && a.Account != b.Account
        && !b.IsTransfer && !b.IsRefund && b.DuplicateOfId is null;

    private static bool AccountsCompatible(Transaction a, Transaction b) =>
        string.IsNullOrEmpty(a.Account) || string.IsNullOrEmpty(b.Account) || a.Account == b.Account;

    private static bool MerchantsCompatible(Transaction a, Transaction b) =>
        string.IsNullOrWhiteSpace(a.Merchant) || string.IsNullOrWhiteSpace(b.Merchant)
        || string.Equals(a.Merchant, b.Merchant, StringComparison.OrdinalIgnoreCase);
}
