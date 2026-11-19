using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Categorization;
using PennyPath.Application.Parsing;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public class TrendTests
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
        "Rs 20,000.00 credited to your A/c XX5678 on 01-09-2026 by salary",
    ];

    [Fact]
    public async Task Trend_lists_each_month_oldest_first_including_empty_months()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        var t = await svc.TrendAsync(Asha, 4, null);

        Assert.Equal(["2026-08", "2026-09", "2026-10", "2026-11"], t.Select(m => m.Month));
        Assert.Equal((0m, 0m), (t[0].Income, t[0].Expense));                  // empty month still present
        Assert.Equal(20000m, t[1].Income);
        Assert.Equal(1299m, t[2].Expense);
        Assert.Equal((15000m, 250m, 14750m), (t[3].Income, t[3].Expense, t[3].Savings));
    }

    [Fact]
    public async Task Each_trend_month_equals_that_months_summary()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        foreach (var m in await svc.TrendAsync(Asha, 6, null))
        {
            var s = await svc.SummaryAsync(Asha, m.Month);
            Assert.Equal((s.Income, s.Expense, s.Savings), (m.Income, m.Expense, m.Savings));
        }
    }

    [Fact]
    public async Task Trend_ends_at_the_requested_month()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        var t = await svc.TrendAsync(Asha, 2, "2026-10");

        Assert.Equal(["2026-09", "2026-10"], t.Select(m => m.Month));
    }

    [Fact]
    public async Task Trend_crosses_a_year_boundary()
    {
        using var db = NewDb();

        var t = await Create(db).TrendAsync(Asha, 3, "2026-01");

        Assert.Equal(["2025-11", "2025-12", "2026-01"], t.Select(m => m.Month));
    }

    [Fact]
    public async Task Trend_leaves_out_duplicates_and_transfers()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Messages[1], "INR 250.00 debited from A/c XX1234 on 02-11-26 to SWIGGY. UPI Ref 4567890123"]);
        Assert.Equal(250m, (await svc.TrendAsync(Asha, 2, null))[1].Expense);

        var original = (await svc.ListAsync(Asha, null)).Single(t => t.DuplicateOfId is null);
        await svc.SetTransferAsync(Asha, original.Id, true);

        Assert.Equal(0m, (await svc.TrendAsync(Asha, 2, null))[1].Expense);
    }

    [Fact]
    public async Task Trend_never_includes_another_users_rows()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Messages);

        Assert.All(await svc.TrendAsync(Ravi, 6, null), m => Assert.Equal(0m, m.Income + m.Expense));
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(13, null)]
    [InlineData(0, null)]
    [InlineData(6, "11-2026")]
    [InlineData(6, "junk")]
    public async Task Bad_parameters_are_rejected(int months, string? to)
    {
        using var db = NewDb();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Create(db).TrendAsync(Asha, months, to));
    }
}
