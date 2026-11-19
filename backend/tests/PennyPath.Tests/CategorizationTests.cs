using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Categorization;
using PennyPath.Application.Parsing;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public class CategorizationTests
{
    private readonly RuleCategorizer _rules = new();

    [Theory]
    [InlineData("Swiggy", "Food & Dining")]
    [InlineData("ZOMATO LTD", "Food & Dining")]
    [InlineData("BigBasket", "Groceries")]
    [InlineData("Uber India", "Transport")]
    [InlineData("Amazon Pay", "Shopping")]
    [InlineData("Netflix.com", "Entertainment")]
    [InlineData("Airtel Postpaid", "Bills & Utilities")]
    [InlineData("Apollo Pharmacy", "Health")]
    public void Known_merchants_get_their_category(string merchant, string expected) =>
        Assert.Equal(expected, _rules.Categorize(merchant, TransactionType.Expense));

    [Theory]
    [InlineData("Coca Cola Stall")]   // "ola" must not match inside "cola"
    [InlineData("Lablab Store")]      // "lab" must not match inside a longer word
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_or_empty_expense_is_uncategorized(string? merchant) =>
        Assert.Equal(Categories.Uncategorized, _rules.Categorize(merchant, TransactionType.Expense));

    [Fact]
    public void Unknown_income_is_income() =>
        Assert.Equal(Categories.Income, _rules.Categorize(null, TransactionType.Income));

    [Fact]
    public void Every_rule_category_is_a_valid_category()
    {
        foreach (var m in new[] { "swiggy", "bigbasket", "uber", "amazon", "airtel", "netflix", "apollo", "makemytrip", "udemy", "rent" })
            Assert.True(Categories.IsValid(_rules.Categorize(m, TransactionType.Expense)), m);
    }

    // ---- service behaviour ----

    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();
    private const string Swiggy = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123";

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TransactionService Create(AppDbContext db) =>
        new(db, new SmsTransactionParser(), new RuleCategorizer(),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Import_categorizes_by_rules()
    {
        using var db = NewDb();
        await Create(db).ImportSmsAsync(Asha, [Swiggy]);

        var tx = await db.Transactions.SingleAsync();
        Assert.Equal("Food & Dining", tx.Category);
        Assert.False(tx.CategoryIsManual);
    }

    [Fact]
    public async Task User_correction_is_saved_and_survives_a_reimport()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var id = (await db.Transactions.SingleAsync()).Id;

        Assert.Equal(CategoryUpdateResult.Updated, await svc.UpdateCategoryAsync(Asha, id, "Groceries"));
        await svc.ImportSmsAsync(Asha, [Swiggy]);

        var tx = await db.Transactions.SingleAsync();
        Assert.Equal("Groceries", tx.Category);
        Assert.True(tx.CategoryIsManual);
    }

    [Fact]
    public async Task Another_users_transaction_cannot_be_changed()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var id = (await db.Transactions.SingleAsync()).Id;

        Assert.Equal(CategoryUpdateResult.NotFound, await svc.UpdateCategoryAsync(Ravi, id, "Groceries"));

        var tx = await db.Transactions.SingleAsync();
        Assert.Equal("Food & Dining", tx.Category);
        Assert.False(tx.CategoryIsManual);
    }

    [Theory]
    [InlineData("NotACategory")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Unknown_category_is_rejected(string? category)
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var id = (await db.Transactions.SingleAsync()).Id;

        Assert.Equal(CategoryUpdateResult.InvalidCategory, await svc.UpdateCategoryAsync(Asha, id, category));
        Assert.Equal("Food & Dining", (await db.Transactions.SingleAsync()).Category);
    }

    [Fact]
    public async Task Missing_transaction_is_not_found()
    {
        using var db = NewDb();
        Assert.Equal(CategoryUpdateResult.NotFound, await Create(db).UpdateCategoryAsync(Asha, Guid.NewGuid(), "Groceries"));
    }
}
