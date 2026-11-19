using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Categorization;
using PennyPath.Application.Parsing;
using PennyPath.Application.Summary;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public class SummaryCalculatorTests
{
    private static SummaryRow Row(TransactionType type, decimal amount, string category = "Food & Dining", string merchant = "Swiggy",
        bool refund = false, bool transfer = false, bool duplicate = false) =>
        new(type, amount, refund, transfer, duplicate, category, merchant);

    [Fact]
    public void Totals_split_income_and_expense_and_savings_is_the_difference()
    {
        var s = SummaryCalculator.Compute("2026-11", [
            Row(TransactionType.Income, 15000, "Income", "salary"),
            Row(TransactionType.Expense, 250),
            Row(TransactionType.Expense, 1299, "Entertainment", "Netflix"),
        ]);

        Assert.Equal(15000m, s.Income);
        Assert.Equal(1549m, s.Expense);
        Assert.Equal(13451m, s.Savings);
        Assert.Equal(3, s.Counted);
    }

    [Fact]
    public void Duplicates_and_transfers_are_left_out_and_counted_as_excluded()
    {
        var s = SummaryCalculator.Compute("2026-11", [
            Row(TransactionType.Expense, 250),
            Row(TransactionType.Expense, 250, duplicate: true),
            Row(TransactionType.Expense, 5000, "Transfers", "", transfer: true),
            Row(TransactionType.Income, 5000, "Transfers", "", transfer: true),
        ]);

        Assert.Equal(250m, s.Expense);
        Assert.Equal(0m, s.Income);
        Assert.Equal(1, s.Counted);
        Assert.Equal(3, s.Excluded);
    }

    [Fact]
    public void A_refund_reduces_expense_and_is_never_income()
    {
        var s = SummaryCalculator.Compute("2026-11", [
            Row(TransactionType.Expense, 250),
            Row(TransactionType.Income, 250, refund: true),
        ]);

        Assert.Equal(0m, s.Income);
        Assert.Equal(0m, s.Expense);
        Assert.Equal(0m, Assert.Single(s.ByMerchant).Amount);
    }

    [Fact]
    public void A_refund_without_a_purchase_in_the_month_makes_net_expense_negative()
    {
        var s = SummaryCalculator.Compute("2026-11", [Row(TransactionType.Income, 100, refund: true)]);

        Assert.Equal(-100m, s.Expense);
        Assert.Equal(0m, s.Income);
    }

    [Fact]
    public void Breakdowns_sum_to_total_expense_and_are_sorted_largest_first()
    {
        var s = SummaryCalculator.Compute("2026-11", [
            Row(TransactionType.Expense, 250, "Food & Dining", "Swiggy"),
            Row(TransactionType.Expense, 100, "Food & Dining", "swiggy"),
            Row(TransactionType.Expense, 900, "Shopping", "Amazon"),
            Row(TransactionType.Expense, 50, "Uncategorized", ""),
        ]);

        Assert.Equal(s.Expense, s.ByCategory.Sum(l => l.Amount));
        Assert.Equal(s.Expense, s.ByMerchant.Sum(l => l.Amount));
        Assert.Equal(["Shopping", "Food & Dining", "Uncategorized"], s.ByCategory.Select(l => l.Name));
        Assert.Equal(350m, s.ByMerchant.Single(l => l.Name.Equals("Swiggy", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(50m, s.ByMerchant.Single(l => l.Name == SummaryCalculator.UnknownPlace).Amount);
    }

    [Fact]
    public void Empty_month_is_all_zero()
    {
        var s = SummaryCalculator.Compute("2026-11", []);

        Assert.Equal((0m, 0m, 0m), (s.Income, s.Expense, s.Savings));
        Assert.Empty(s.ByCategory);
        Assert.Empty(s.ByMerchant);
    }
}

public class SummaryServiceTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TransactionService Create(AppDbContext db) =>
        new(db, new SmsTransactionParser(), new RuleCategorizer(),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)));

    private static readonly string[] Messages =
    [
        "Dear Customer, Rs. 15,000.00 credited to your A/c XX5678 on 01-11-2026 by salary",
        "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123",
        "INR 1,299.00 debited from A/c XX4321 on 28-10-26 towards NETFLIX. Available balance INR 5,000",
        "Rs 450 spent on card XX9876 at AMAZON on 02-Nov-26. Avl bal Rs 12,000.00",
    ];

    [Fact]
    public async Task Summary_equals_the_sum_of_the_listed_rows_for_the_month()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        var summary = await svc.SummaryAsync(Asha, "2026-11");
        var list = await svc.ListAsync(Asha, "2026-11");

        Assert.Equal(list.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount), summary.Income);
        Assert.Equal(list.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount), summary.Expense);
        Assert.Equal(summary.Income - summary.Expense, summary.Savings);
        Assert.Equal(list.Count, summary.Counted + summary.Excluded);
    }

    [Fact]
    public async Task Summary_covers_only_the_requested_month()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        var oct = await svc.SummaryAsync(Asha, "2026-10");

        Assert.Equal(1299m, oct.Expense);
        Assert.Equal(0m, oct.Income);
        Assert.Equal("2026-10", oct.Month);
    }

    [Fact]
    public async Task Summary_defaults_to_the_current_month()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        Assert.Equal("2026-11", (await svc.SummaryAsync(Asha, null)).Month);
    }

    [Fact]
    public async Task Summary_leaves_out_flagged_duplicates_and_transfers()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Messages[1], "INR 250.00 debited from A/c XX1234 on 02-11-26 to SWIGGY. UPI Ref 4567890123", Messages[3]]);
        var amazon = (await svc.ListAsync(Asha, null)).Single(t => t.Merchant == "Amazon");
        await svc.SetTransferAsync(Asha, amazon.Id, true);

        var s = await svc.SummaryAsync(Asha, "2026-11");

        Assert.Equal(250m, s.Expense);
        Assert.Equal(1, s.Counted);
        Assert.Equal(2, s.Excluded);
    }

    [Fact]
    public async Task Summary_never_includes_another_users_rows()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        var s = await svc.SummaryAsync(Ravi, "2026-11");

        Assert.Equal(0m, s.Income + s.Expense);
        Assert.Equal(0, s.Counted + s.Excluded);
    }

    [Theory]
    [InlineData("11-2026")]
    [InlineData("2026-13")]
    [InlineData("junk")]
    public async Task Malformed_month_is_rejected(string month)
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<ArgumentException>(() => Create(db).SummaryAsync(Asha, month));
    }
}
