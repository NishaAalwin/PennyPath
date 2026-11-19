using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Categorization;
using PennyPath.Application.Parsing;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public class QueryAndEditTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TransactionService Create(AppDbContext db) =>
        new(db, new SmsTransactionParser(), new RuleCategorizer(),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)));

    private static Transaction Tx(Guid user, string merchant, decimal amount, string date, TransactionType type = TransactionType.Expense,
        string category = "Uncategorized") => new()
    {
        UserId = user, Merchant = merchant, Amount = amount, Date = DateOnly.Parse(date), Type = type, Category = category,
        RawHash = Guid.NewGuid().ToString("N"), Source = TransactionSource.Sms,
    };

    private static async Task<TransactionService> Seeded(AppDbContext db)
    {
        db.Transactions.AddRange(
            Tx(Asha, "Swiggy", 250, "2026-11-02", category: "Food & Dining"),
            Tx(Asha, "Zomato", 320, "2026-11-01", category: "Food & Dining"),
            Tx(Asha, "Amazon", 450, "2026-10-28", category: "Shopping"),
            Tx(Asha, "salary", 15000, "2026-11-01", TransactionType.Income, "Income"),
            Tx(Asha, "Netflix", 1299, "2026-10-15", category: "Entertainment"),
            Tx(Ravi, "Swiggy", 999, "2026-11-02", category: "Food & Dining"));
        await db.SaveChangesAsync();
        return Create(db);
    }

    private static TransactionQuery Q(string? month = null, string? search = null, string? category = null,
        TransactionType? type = null, DateOnly? from = null, DateOnly? to = null, int page = 1, int size = 50) =>
        new(month, from, to, search, category, type, page, size);

    // ---- search and filter ----

    [Fact]
    public async Task Search_matches_the_merchant_ignoring_case_and_part_of_a_word()
    {
        using var db = NewDb();
        var svc = await Seeded(db);

        Assert.Equal(["Swiggy"], (await svc.QueryAsync(Asha, Q(search: "SWIG"))).Items.Select(t => t.Merchant));
        Assert.Equal(1, (await svc.QueryAsync(Asha, Q(search: "  netflix  "))).Total);
    }

    [Fact]
    public async Task Search_by_a_number_matches_the_amount()
    {
        using var db = NewDb();
        var svc = await Seeded(db);

        var r = await svc.QueryAsync(Asha, Q(search: "450"));

        Assert.Equal(["Amazon"], r.Items.Select(t => t.Merchant));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("'; drop table Transactions; --")]
    public async Task Search_treats_wildcards_and_quotes_as_plain_text(string term)
    {
        using var db = NewDb();
        var svc = await Seeded(db);

        Assert.Equal(0, (await svc.QueryAsync(Asha, Q(search: term))).Total);
        Assert.Equal(5, await db.Transactions.CountAsync(t => t.UserId == Asha));
    }

    [Fact]
    public async Task Filters_by_type_category_month_and_date_range()
    {
        using var db = NewDb();
        var svc = await Seeded(db);

        Assert.Equal(["salary"], (await svc.QueryAsync(Asha, Q(type: TransactionType.Income))).Items.Select(t => t.Merchant));
        Assert.Equal(2, (await svc.QueryAsync(Asha, Q(category: "Food & Dining"))).Total);
        Assert.Equal(2, (await svc.QueryAsync(Asha, Q(month: "2026-10"))).Total);
        var range = await svc.QueryAsync(Asha, Q(from: new DateOnly(2026, 10, 20), to: new DateOnly(2026, 11, 1)));
        Assert.Equal(["Amazon", "Zomato", "salary"], range.Items.Select(t => t.Merchant).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Filters_combine_with_and()
    {
        using var db = NewDb();
        var svc = await Seeded(db);

        var r = await svc.QueryAsync(Asha, Q(month: "2026-11", category: "Food & Dining", search: "zom", type: TransactionType.Expense));

        Assert.Equal(["Zomato"], r.Items.Select(t => t.Merchant));
    }

    [Fact]
    public async Task Results_are_newest_first_and_only_the_users_own()
    {
        using var db = NewDb();
        var svc = await Seeded(db);

        var r = await svc.QueryAsync(Asha, Q());

        Assert.Equal(5, r.Total);
        Assert.Equal(r.Items.Select(t => t.Date).OrderDescending(), r.Items.Select(t => t.Date));
        Assert.DoesNotContain(r.Items, t => t.Amount == 999);
    }

    // ---- paging ----

    [Fact]
    public async Task Paging_returns_every_row_exactly_once_in_a_stable_order()
    {
        using var db = NewDb();
        db.Transactions.AddRange(Enumerable.Range(0, 120).Select(i => Tx(Asha, $"Shop {i}", 10 + i, "2026-11-01")));
        await db.SaveChangesAsync();
        var svc = Create(db);

        var p1 = await svc.QueryAsync(Asha, Q(page: 1, size: 50));
        var p2 = await svc.QueryAsync(Asha, Q(page: 2, size: 50));
        var p3 = await svc.QueryAsync(Asha, Q(page: 3, size: 50));
        var p4 = await svc.QueryAsync(Asha, Q(page: 4, size: 50));

        Assert.Equal((120, 50, 50, 20, 0), (p1.Total, p1.Items.Count, p2.Items.Count, p3.Items.Count, p4.Items.Count));
        var ids = p1.Items.Concat(p2.Items).Concat(p3.Items).Select(t => t.Id).ToList();
        Assert.Equal(120, ids.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Page_and_size_limits_are_enforced(int page, int size)
    {
        using var db = NewDb();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Create(db).QueryAsync(Asha, Q(page: page, size: size)));
    }

    [Fact]
    public async Task Bad_filter_values_are_rejected()
    {
        using var db = NewDb();
        var svc = Create(db);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.QueryAsync(Asha, Q(month: "11-2026")));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.QueryAsync(Asha, Q(category: "Nope")));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => svc.QueryAsync(Asha, Q(search: new string('x', TransactionService.MaxSearchLength + 1))));
    }

    // ---- edit ----

    private static async Task<(TransactionService Svc, Guid Id)> OneRow(AppDbContext db, Transaction? row = null)
    {
        row ??= Tx(Asha, "Swiggy", 250, "2026-11-02", category: "Food & Dining");
        db.Transactions.Add(row);
        await db.SaveChangesAsync();
        return (Create(db), row.Id);
    }

    [Fact]
    public async Task Edit_changes_only_the_fields_that_are_given_and_returns_the_row()
    {
        using var db = NewDb();
        var (svc, id) = await OneRow(db);

        var r = await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, 275.5m, null, null));

        Assert.True(r.Found);
        Assert.Null(r.Error);
        Assert.Equal((275.5m, "Swiggy", new DateOnly(2026, 11, 2)), (r.Transaction!.Amount, r.Transaction.Merchant, r.Transaction.Date));
        Assert.Equal(275.5m, (await db.Transactions.SingleAsync()).Amount);
    }

    [Fact]
    public async Task Changing_the_merchant_recategorizes_unless_the_user_chose_a_category()
    {
        using var db = NewDb();
        var (svc, id) = await OneRow(db);

        var auto = await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest("  Uber ", null, null, null));
        Assert.Equal(("Uber", "Transport"), (auto.Transaction!.Merchant, auto.Transaction.Category));

        await svc.UpdateCategoryAsync(Asha, id, "Rent");
        var manual = await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest("Amazon", null, null, null));
        Assert.Equal("Rent", manual.Transaction!.Category);
    }

    [Fact]
    public async Task Changing_the_type_to_expense_clears_the_refund_flag()
    {
        using var db = NewDb();
        var row = Tx(Asha, "Swiggy", 250, "2026-11-02", TransactionType.Income, "Income");
        row.IsRefund = true;
        var (svc, id) = await OneRow(db, row);

        var r = await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, null, null, TransactionType.Expense));

        Assert.False(r.Transaction!.IsRefund);
        Assert.Equal(TransactionType.Expense, r.Transaction.Type);
    }

    [Fact]
    public async Task A_real_edit_clears_guesses_about_the_row_and_about_rows_pointing_at_it()
    {
        using var db = NewDb();
        var original = Tx(Asha, "Swiggy", 250, "2026-11-02");
        var dup = Tx(Asha, "Swiggy", 250, "2026-11-02");
        dup.DuplicateOfId = original.Id;
        var refund = Tx(Asha, "Swiggy", 250, "2026-11-02", TransactionType.Income);
        refund.IsRefund = true;
        refund.RefundOfId = original.Id;
        db.Transactions.AddRange(original, dup, refund);
        await db.SaveChangesAsync();
        var svc = Create(db);

        await svc.UpdateAsync(Asha, original.Id, new UpdateTransactionRequest(null, 260m, null, null));

        Assert.Null((await db.Transactions.FindAsync(dup.Id))!.DuplicateOfId);
        Assert.Null((await db.Transactions.FindAsync(refund.Id))!.RefundOfId);
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_keeps_the_flags()
    {
        using var db = NewDb();
        var original = Tx(Asha, "Swiggy", 250, "2026-11-02");
        var dup = Tx(Asha, "Swiggy", 250, "2026-11-02");
        dup.DuplicateOfId = original.Id;
        db.Transactions.AddRange(original, dup);
        await db.SaveChangesAsync();

        await Create(db).UpdateAsync(Asha, dup.Id, new UpdateTransactionRequest("Swiggy", 250m, null, null));

        Assert.Equal(original.Id, (await db.Transactions.FindAsync(dup.Id))!.DuplicateOfId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000000000)]
    [InlineData(10.123)]
    public async Task Bad_amounts_are_refused_and_nothing_changes(double amount)
    {
        using var db = NewDb();
        var (svc, id) = await OneRow(db);

        var r = await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest("Changed", (decimal)amount, null, null));

        Assert.True(r.Found);
        Assert.NotNull(r.Error);
        var row = await db.Transactions.SingleAsync();
        Assert.Equal(("Swiggy", 250m), (row.Merchant, row.Amount));
    }

    [Fact]
    public async Task Bad_merchant_date_type_and_empty_requests_are_refused()
    {
        using var db = NewDb();
        var (svc, id) = await OneRow(db);

        Assert.NotNull((await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(new string('x', 201), null, null, null))).Error);
        Assert.NotNull((await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest("bad\u0007name", null, null, null))).Error);
        Assert.NotNull((await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, null, new DateOnly(1999, 12, 31), null))).Error);
        Assert.NotNull((await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, null, new DateOnly(2026, 11, 10), null))).Error);
        Assert.NotNull((await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, null, null, (TransactionType)7))).Error);
        Assert.NotNull((await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, null, null, null))).Error);
    }

    [Fact]
    public async Task Merchant_may_be_cleared_and_the_date_may_be_up_to_tomorrow()
    {
        using var db = NewDb();
        var (svc, id) = await OneRow(db);

        var r = await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest("", null, new DateOnly(2026, 11, 4), null));

        Assert.Null(r.Error);
        Assert.Equal(("", new DateOnly(2026, 11, 4)), (r.Transaction!.Merchant, r.Transaction.Date));
    }

    [Fact]
    public async Task Another_users_or_missing_transaction_is_not_found_and_untouched()
    {
        using var db = NewDb();
        var (svc, id) = await OneRow(db);

        var other = await svc.UpdateAsync(Ravi, id, new UpdateTransactionRequest("Hacked", 1m, null, null));
        var missing = await svc.UpdateAsync(Asha, Guid.NewGuid(), new UpdateTransactionRequest("x", null, null, null));

        Assert.False(other.Found);
        Assert.False(missing.Found);
        Assert.Equal("Swiggy", (await db.Transactions.SingleAsync()).Merchant);
    }

    [Fact]
    public async Task An_edited_row_keeps_its_import_hash_so_reimporting_does_not_bring_it_back()
    {
        using var db = NewDb();
        var svc = Create(db);
        const string sms = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123";
        await svc.ImportSmsAsync(Asha, [sms]);
        var id = (await db.Transactions.SingleAsync()).Id;

        await svc.UpdateAsync(Asha, id, new UpdateTransactionRequest(null, 300m, null, null));
        var again = await svc.ImportSmsAsync(Asha, [sms]);

        Assert.Equal(0, again.Imported);
        Assert.Equal(300m, (await db.Transactions.SingleAsync()).Amount);
    }
}
