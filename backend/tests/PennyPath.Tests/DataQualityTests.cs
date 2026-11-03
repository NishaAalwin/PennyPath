using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Categorization;
using PennyPath.Application.Parsing;
using PennyPath.Application.Reconciliation;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public class DataQualityParserTests
{
    private readonly SmsTransactionParser _parser = new();
    private static readonly DateOnly Today = new(2026, 11, 3);

    [Fact]
    public void Refund_is_income_flagged_as_refund_with_merchant()
    {
        var p = _parser.Parse("Refund of Rs 250.00 credited to A/c XX1234 on 05-11-26 from SWIGGY.", Today)!;
        Assert.True(p.IsRefund);
        Assert.Equal(TransactionType.Income, p.Type);
        Assert.Equal("Swiggy", p.Merchant);
    }

    [Fact]
    public void Reversal_wording_is_also_a_refund()
    {
        var p = _parser.Parse("Rs 250.00 reversed to A/c XX1234 on 05-11-26 for txn at SWIGGY.", Today)!;
        Assert.True(p.IsRefund);
        Assert.Equal(TransactionType.Income, p.Type);
    }

    [Fact]
    public void Pending_refund_is_not_a_transaction() =>
        Assert.Null(_parser.Parse("Refund of Rs 250 will be credited to your account in 5 days", Today));

    [Fact]
    public void Normal_credit_is_not_a_refund() =>
        Assert.False(_parser.Parse("Rs. 15,000.00 credited to your A/c XX5678 on 01-11-2026 by salary", Today)!.IsRefund);

    [Fact]
    public void Parsed_result_carries_only_amount_direction_date_merchant_and_refund_flag() =>
        Assert.Equal(
            ["Amount", "Type", "Date", "Merchant", "IsRefund"],
            typeof(ParsedTransaction).GetProperties().Select(p => p.Name).Where(n => n != "EqualityContract"));
}

public class TransactionReconcilerTests
{
    private static readonly Guid U = Guid.NewGuid();
    private static readonly DateOnly D = new(2026, 11, 2);

    private static Transaction Tx(decimal amount, TransactionType type, string merchant = "Swiggy",
        DateOnly? date = null, bool refund = false) => new()
    {
        UserId = U, Amount = amount, Type = type, Merchant = merchant, Date = date ?? D, IsRefund = refund, Category = "Food & Dining",
    };

    private static void Run(IEnumerable<Transaction> fresh, params Transaction[] existing) =>
        TransactionReconciler.Reconcile(fresh.ToList(), existing);

    [Fact]
    public void Same_amount_day_direction_and_merchant_is_flagged_as_a_possible_duplicate()
    {
        var a = Tx(250, TransactionType.Expense);
        var b = Tx(250, TransactionType.Expense, "SWIGGY");
        Run([b], a);
        Assert.Equal(a.Id, b.DuplicateOfId);
    }

    [Theory]
    [InlineData(300, "Swiggy", 0, TransactionType.Expense)]   // other amount
    [InlineData(250, "Zomato", 0, TransactionType.Expense)]   // other merchant
    [InlineData(250, "Swiggy", 1, TransactionType.Expense)]   // other day
    [InlineData(250, "Swiggy", 0, TransactionType.Income)]    // other direction
    [InlineData(250, "", 0, TransactionType.Expense)]         // unknown merchant: too little to call it a duplicate
    public void Near_misses_are_not_duplicates(decimal amount, string merchant, int dayOffset, TransactionType type)
    {
        var a = Tx(250, TransactionType.Expense);
        var b = Tx(amount, type, merchant, D.AddDays(dayOffset));
        Run([b], a);
        Assert.Null(b.DuplicateOfId);
    }

    [Fact]
    public void Two_unknown_merchants_are_never_duplicates()
    {
        var a = Tx(500, TransactionType.Income, "");
        var b = Tx(500, TransactionType.Income, "");
        Run([a, b]);
        Assert.Null(b.DuplicateOfId);
    }

    [Fact]
    public void Two_new_rows_in_one_batch_can_duplicate_each_other()
    {
        var a = Tx(250, TransactionType.Expense);
        var b = Tx(250, TransactionType.Expense);
        Run([a, b]);
        Assert.Null(a.DuplicateOfId);
        Assert.Equal(a.Id, b.DuplicateOfId);
    }

    [Fact]
    public void Refund_links_to_the_matching_earlier_expense_and_takes_its_category()
    {
        var expense = Tx(250, TransactionType.Expense, date: D);
        expense.Category = "Shopping";
        var refund = Tx(250, TransactionType.Income, "SWIGGY", D.AddDays(3), refund: true);
        Run([refund], expense);

        Assert.Equal(expense.Id, refund.RefundOfId);
        Assert.Equal("Shopping", refund.Category);
    }

    [Fact]
    public void One_expense_is_refunded_only_once()
    {
        var expense = Tx(250, TransactionType.Expense);
        var r1 = Tx(250, TransactionType.Income, date: D.AddDays(1), refund: true);
        var r2 = Tx(250, TransactionType.Income, date: D.AddDays(2), refund: true);
        Run([r1, r2], expense);

        Assert.Equal(expense.Id, r1.RefundOfId);
        Assert.Null(r2.RefundOfId);
        Assert.True(r2.IsRefund);
    }

    [Theory]
    [InlineData(300, "Swiggy", 3)]    // different amount (partial refunds are not matched)
    [InlineData(250, "Zomato", 3)]    // different merchant
    [InlineData(250, "Swiggy", 90)]   // outside the 60-day window
    [InlineData(250, "Swiggy", -1)]   // refund before the purchase
    public void Refund_that_does_not_match_stays_unlinked(decimal amount, string merchant, int days)
    {
        var expense = Tx(250, TransactionType.Expense);
        var refund = Tx(amount, TransactionType.Income, merchant, D.AddDays(days), refund: true);
        Run([refund], expense);
        Assert.Null(refund.RefundOfId);
    }

    [Fact]
    public void Transfers_are_never_marked_automatically()
    {
        var debit = Tx(5000, TransactionType.Expense, "");
        var credit = Tx(5000, TransactionType.Income, "", D.AddDays(1));
        Run([debit, credit]);

        Assert.False(debit.IsTransfer);
        Assert.False(credit.IsTransfer);
    }

    [Fact]
    public void A_manual_category_is_kept_when_marked_as_a_transfer()
    {
        var t = Tx(5000, TransactionType.Expense, "");
        t.Category = "Rent";
        t.CategoryIsManual = true;
        TransactionReconciler.MarkTransfer(t);

        Assert.True(t.IsTransfer);
        Assert.Equal("Rent", t.Category);
    }
}

public class DataQualityServiceTests
{
    private const string Swiggy = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123";
    private const string SwiggySecondMessage = "INR 250.00 debited from A/c XX1234 on 02-11-26 to SWIGGY. UPI Ref 4567890123";
    private const string SwiggyOtherDay = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 03/11/26 Ref 9999999999";
    private const string Refund = "Refund of Rs 250.00 credited to A/c XX1234 on 05-11-26 from SWIGGY.";
    private const string Garbage = "Hello, your parcel is out for delivery";

    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static TransactionService Create(AppDbContext db) =>
        new(db, new SmsTransactionParser(), new RuleCategorizer(),
            new FakeTimeProvider(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Second_message_about_the_same_payment_is_flagged_not_dropped()
    {
        using var db = NewDb();
        var svc = Create(db);
        var r1 = await svc.ImportSmsAsync(Asha, [Swiggy]);
        var r2 = await svc.ImportSmsAsync(Asha, [SwiggySecondMessage]);

        Assert.Equal(0, r1.PossibleDuplicates);
        Assert.Equal(1, r2.PossibleDuplicates);
        var rows = await svc.ListAsync(Asha, null);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, t => t.DuplicateOfId is not null);
    }

    [Fact]
    public async Task Same_shop_on_another_day_is_a_separate_purchase()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggyOtherDay]);

        Assert.All(await svc.ListAsync(Asha, null), t => Assert.Null(t.DuplicateOfId));
    }

    [Fact]
    public async Task Duplicate_detection_never_crosses_users()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var r = await svc.ImportSmsAsync(Ravi, [SwiggySecondMessage]);

        Assert.Equal(0, r.PossibleDuplicates);
        Assert.All(await svc.ListAsync(Ravi, null), t => Assert.Null(t.DuplicateOfId));
    }

    [Fact]
    public async Task User_can_say_not_a_duplicate_and_it_stays_that_way()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggySecondMessage]);
        var flagged = (await svc.ListAsync(Asha, null)).Single(t => t.DuplicateOfId is not null);

        Assert.Equal(TransactionActionResult.Done, await svc.ClearDuplicateAsync(Asha, flagged.Id));
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggySecondMessage]);   // re-import is a no-op

        Assert.All(await svc.ListAsync(Asha, null), t => Assert.Null(t.DuplicateOfId));
    }

    [Fact]
    public async Task Refund_links_to_the_expense_and_inherits_its_category()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        await svc.ImportSmsAsync(Asha, [Refund]);

        var rows = await svc.ListAsync(Asha, null);
        var expense = rows.Single(t => t.Type == TransactionType.Expense);
        var refund = rows.Single(t => t.IsRefund);
        Assert.Equal(expense.Id, refund.RefundOfId);
        Assert.Equal(expense.Category, refund.Category);
    }

    [Fact]
    public async Task User_can_mark_and_unmark_a_transfer()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var id = (await svc.ListAsync(Asha, null)).Single().Id;

        await svc.SetTransferAsync(Asha, id, true);
        var marked = (await svc.ListAsync(Asha, null)).Single();
        Assert.True(marked.IsTransfer);
        Assert.Equal("Transfers", marked.Category);

        await svc.SetTransferAsync(Asha, id, false);
        var unmarked = (await svc.ListAsync(Asha, null)).Single();
        Assert.False(unmarked.IsTransfer);
        Assert.Equal("Food & Dining", unmarked.Category);
    }

    [Fact]
    public async Task Unreadable_messages_are_reported_once_and_never_stored()
    {
        using var db = NewDb();
        var svc = Create(db);

        var r = await svc.ImportSmsAsync(Asha, [Garbage, Swiggy]);

        Assert.Single(r.Unparsed);
        Assert.Equal(1, await db.Transactions.CountAsync());
        Assert.DoesNotContain(db.Model.GetEntityTypes(), e => e.ClrType.Name.Contains("Unparsed"));
    }

    [Fact]
    public async Task Stored_rows_hold_no_message_text_or_account_details()
    {
        using var db = NewDb();
        await Create(db).ImportSmsAsync(Asha, [Swiggy]);

        var columns = db.Model.FindEntityType(typeof(Transaction))!.GetProperties().Select(p => p.Name).ToHashSet();

        Assert.DoesNotContain("Account", columns);
        Assert.DoesNotContain("Reference", columns);
        Assert.DoesNotContain("Text", columns);
        Assert.DoesNotContain("Body", columns);
        var row = await db.Transactions.SingleAsync();
        Assert.DoesNotContain("1234", row.Merchant + row.Category);
        Assert.DoesNotContain("4567890123", row.Merchant + row.Category);
    }

    [Fact]
    public async Task Delete_removes_the_row_and_unflags_rows_that_pointed_at_it()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggySecondMessage]);
        var original = (await svc.ListAsync(Asha, null)).Single(t => t.DuplicateOfId is null);

        Assert.Equal(TransactionActionResult.Done, await svc.DeleteAsync(Asha, original.Id));

        var left = await svc.ListAsync(Asha, null);
        Assert.Single(left);
        Assert.Null(left[0].DuplicateOfId);
    }

    [Fact]
    public async Task Other_users_cannot_change_transfer_duplicate_or_delete()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var id = (await svc.ListAsync(Asha, null)).Single().Id;

        Assert.Equal(TransactionActionResult.NotFound, await svc.SetTransferAsync(Ravi, id, true));
        Assert.Equal(TransactionActionResult.NotFound, await svc.ClearDuplicateAsync(Ravi, id));
        Assert.Equal(TransactionActionResult.NotFound, await svc.DeleteAsync(Ravi, id));
        var row = Assert.Single(await svc.ListAsync(Asha, null));
        Assert.False(row.IsTransfer);
    }
}
