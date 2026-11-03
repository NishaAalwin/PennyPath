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

    [Theory]
    [InlineData("Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123", "4567890123")]
    [InlineData("Rs.500.00 credited to a/c **2222 on 30-10-26 from RAHUL SHARMA. UPI Ref 998877", "998877")]
    [InlineData("INR 99 debited from A/c XX1 on 01-11-26 to ZOMATO. UTR: AB1234567890", "AB1234567890")]
    public void Reference_numbers_are_extracted(string sms, string expected) =>
        Assert.Equal(expected, _parser.Parse(sms, Today)!.Reference);

    [Fact]
    public void Message_without_reference_has_none() =>
        Assert.Null(_parser.Parse("Rs 450 spent on card XX9876 at AMAZON on 02-Nov-26", Today)!.Reference);

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
}

public class TransactionReconcilerTests
{
    private static readonly Guid U = Guid.NewGuid();
    private static readonly DateOnly D = new(2026, 11, 2);

    private static Transaction Tx(decimal amount, TransactionType type, string merchant = "Swiggy", string? account = "1234",
        string? reference = null, DateOnly? date = null, bool refund = false) => new()
    {
        UserId = U, Amount = amount, Type = type, Merchant = merchant, Account = account,
        Reference = reference, Date = date ?? D, IsRefund = refund, Category = "Food & Dining",
    };

    private static void Run(IEnumerable<Transaction> fresh, params Transaction[] existing) =>
        TransactionReconciler.Reconcile(fresh.ToList(), existing);

    [Fact]
    public void Same_reference_is_a_duplicate_even_on_another_day()
    {
        var a = Tx(250, TransactionType.Expense, reference: "REF123456");
        var b = Tx(250, TransactionType.Expense, reference: "ref123456", date: D.AddDays(1));
        Run([b], a);
        Assert.Equal(a.Id, b.DuplicateOfId);
    }

    [Fact]
    public void Different_references_are_two_real_payments()
    {
        var a = Tx(250, TransactionType.Expense, reference: "REF111111");
        var b = Tx(250, TransactionType.Expense, reference: "REF222222");
        Run([b], a);
        Assert.Null(b.DuplicateOfId);
    }

    [Fact]
    public void Same_amount_day_account_merchant_without_references_is_flagged()
    {
        var a = Tx(250, TransactionType.Expense);
        var b = Tx(250, TransactionType.Expense);
        Run([b], a);
        Assert.Equal(a.Id, b.DuplicateOfId);
    }

    [Theory]
    [InlineData(300, "Swiggy", "1234", 0)]   // other amount
    [InlineData(250, "Zomato", "1234", 0)]   // other merchant
    [InlineData(250, "Swiggy", "9999", 0)]   // other account
    [InlineData(250, "Swiggy", "1234", 1)]   // other day
    public void Near_misses_are_not_duplicates(decimal amount, string merchant, string account, int dayOffset)
    {
        var a = Tx(250, TransactionType.Expense);
        var b = Tx(amount, TransactionType.Expense, merchant, account, date: D.AddDays(dayOffset));
        Run([b], a);
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
        var refund = Tx(250, TransactionType.Income, "SWIGGY", date: D.AddDays(3), refund: true);
        Run([refund], expense);

        Assert.Equal(expense.Id, refund.RefundOfId);
        Assert.Equal("Shopping", refund.Category);
    }

    [Fact]
    public void One_expense_is_refunded_only_once()
    {
        var expense = Tx(250, TransactionType.Expense);
        var r1 = Tx(250, TransactionType.Income, date: D.AddDays(1), refund: true, reference: "RF1111111");
        var r2 = Tx(250, TransactionType.Income, date: D.AddDays(2), refund: true, reference: "RF2222222");
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
        var refund = Tx(amount, TransactionType.Income, merchant, date: D.AddDays(days), refund: true);
        Run([refund], expense);
        Assert.Null(refund.RefundOfId);
    }

    [Fact]
    public void Matching_debit_and_credit_on_two_own_accounts_become_a_transfer()
    {
        var debit = Tx(5000, TransactionType.Expense, "", "1234");
        var credit = Tx(5000, TransactionType.Income, "", "5678", date: D.AddDays(1));
        Run([debit, credit]);

        Assert.True(debit.IsTransfer);
        Assert.True(credit.IsTransfer);
        Assert.Equal("Transfers", debit.Category);
    }

    [Fact]
    public void Transfer_pairs_with_an_already_stored_row()
    {
        var stored = Tx(5000, TransactionType.Expense, "", "1234");
        var credit = Tx(5000, TransactionType.Income, "", "5678");
        Run([credit], stored);

        Assert.True(stored.IsTransfer);
        Assert.True(credit.IsTransfer);
    }

    [Fact]
    public void Ambiguous_candidates_are_not_marked_as_transfers()
    {
        var debit = Tx(5000, TransactionType.Expense, "", "1234");
        var credit1 = Tx(5000, TransactionType.Income, "", "5678");
        var credit2 = Tx(5000, TransactionType.Income, "", "9012");
        Run([debit, credit1, credit2]);

        Assert.False(debit.IsTransfer);
        Assert.False(credit1.IsTransfer);
        Assert.False(credit2.IsTransfer);
    }

    [Fact]
    public void Same_account_or_unknown_account_is_never_a_transfer()
    {
        var debit = Tx(5000, TransactionType.Expense, "", "1234");
        var sameAcct = Tx(5000, TransactionType.Income, "", "1234");
        var noAcct = Tx(5000, TransactionType.Income, "", null);
        Run([debit, sameAcct, noAcct]);

        Assert.False(debit.IsTransfer);
        Assert.False(sameAcct.IsTransfer);
        Assert.False(noAcct.IsTransfer);
    }

    [Fact]
    public void A_manual_category_is_kept_on_a_transfer()
    {
        var debit = Tx(5000, TransactionType.Expense, "", "1234");
        debit.Category = "Rent";
        debit.CategoryIsManual = true;
        var credit = Tx(5000, TransactionType.Income, "", "5678");
        Run([credit], debit);

        Assert.True(debit.IsTransfer);
        Assert.Equal("Rent", debit.Category);
    }

    [Fact]
    public void A_flagged_duplicate_does_not_take_part_in_transfers()
    {
        var debit = Tx(5000, TransactionType.Expense, "", "1234");
        var credit = Tx(5000, TransactionType.Income, "", "5678");
        var credit2 = Tx(5000, TransactionType.Income, "", "5678");   // same payment, second message
        Run([debit, credit, credit2]);

        Assert.Equal(credit.Id, credit2.DuplicateOfId);
        Assert.False(credit2.IsTransfer);
        Assert.True(debit.IsTransfer);
        Assert.True(credit.IsTransfer);
    }
}

public class DataQualityServiceTests
{
    private const string Swiggy = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123";
    private const string SwiggyOtherMessageSameRef = "INR 250.00 debited from A/c XX1234 on 02-11-26 to SWIGGY. UPI Ref 4567890123";
    private const string SwiggyOtherRef = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 9999999999";
    private const string Refund = "Refund of Rs 250.00 credited to A/c XX1234 on 05-11-26 from SWIGGY.";
    private const string TransferOut = "Rs 5,000.00 debited from A/c XX1234 on 03-11-26 to A/c XX5678";
    private const string TransferIn = "Rs 5,000.00 credited to A/c XX5678 on 03-11-26";
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
        var r2 = await svc.ImportSmsAsync(Asha, [SwiggyOtherMessageSameRef]);

        Assert.Equal(0, r1.PossibleDuplicates);
        Assert.Equal(1, r2.PossibleDuplicates);
        var rows = await svc.ListAsync(Asha, null);
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, t => t.DuplicateOfId is not null);
    }

    [Fact]
    public async Task Two_real_purchases_with_different_references_are_both_clean()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggyOtherRef]);

        Assert.All(await svc.ListAsync(Asha, null), t => Assert.Null(t.DuplicateOfId));
    }

    [Fact]
    public async Task Duplicate_detection_never_crosses_users()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        var r = await svc.ImportSmsAsync(Ravi, [SwiggyOtherMessageSameRef]);

        Assert.Equal(0, r.PossibleDuplicates);
        Assert.All(await svc.ListAsync(Ravi, null), t => Assert.Null(t.DuplicateOfId));
    }

    [Fact]
    public async Task User_can_say_not_a_duplicate_and_it_stays_that_way()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggyOtherMessageSameRef]);
        var flagged = (await svc.ListAsync(Asha, null)).Single(t => t.DuplicateOfId is not null);

        Assert.Equal(TransactionActionResult.Done, await svc.ClearDuplicateAsync(Asha, flagged.Id));
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggyOtherMessageSameRef]);   // re-import is a no-op

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
    public async Task Transfer_between_own_accounts_is_marked_on_both_rows()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [TransferOut]);
        await svc.ImportSmsAsync(Asha, [TransferIn]);

        var rows = await svc.ListAsync(Asha, null);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, t => { Assert.True(t.IsTransfer); Assert.Equal("Transfers", t.Category); });
    }

    [Fact]
    public async Task User_can_unmark_and_mark_a_transfer()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [TransferOut, TransferIn]);
        var id = (await svc.ListAsync(Asha, null))[0].Id;

        await svc.SetTransferAsync(Asha, id, false);
        var unmarked = (await svc.ListAsync(Asha, null)).Single(t => t.Id == id);
        Assert.False(unmarked.IsTransfer);
        Assert.NotEqual("Transfers", unmarked.Category);

        await svc.SetTransferAsync(Asha, id, true);
        Assert.True((await svc.ListAsync(Asha, null)).Single(t => t.Id == id).IsTransfer);
    }

    [Fact]
    public async Task Unparsed_messages_are_kept_for_review_once()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Garbage, Swiggy]);
        await svc.ImportSmsAsync(Asha, ["  " + Garbage.ToUpperInvariant() + "  "]);

        var list = await svc.ListUnparsedAsync(Asha);
        Assert.Single(list);
        Assert.Equal(Garbage, list[0].Text);
        Assert.Empty(await svc.ListUnparsedAsync(Ravi));
    }

    [Fact]
    public async Task Stored_unparsed_text_is_capped_at_500_characters()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, ["hello " + new string('x', 1500)]);

        Assert.Equal(UnparsedMessage.MaxTextLength, (await svc.ListUnparsedAsync(Asha)).Single().Text.Length);
    }

    [Fact]
    public async Task Stored_unparsed_count_is_capped_per_user()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, Enumerable.Range(0, 250).Select(i => $"junk message {i}").Take(TransactionService.MaxMessages).ToList());

        Assert.Equal(TransactionService.MaxStoredUnparsedPerUser, (await svc.ListUnparsedAsync(Asha)).Count);
    }

    [Fact]
    public async Task A_message_that_parses_later_leaves_the_review_list()
    {
        using var db = NewDb();
        var svc = Create(db);
        db.UnparsedMessages.Add(new UnparsedMessage
        {
            UserId = Asha, Text = Swiggy,
            RawHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Swiggy.ToLowerInvariant()))),
        });
        await db.SaveChangesAsync();

        await svc.ImportSmsAsync(Asha, [Swiggy]);

        Assert.Empty(await svc.ListUnparsedAsync(Asha));
    }

    [Fact]
    public async Task Dismiss_removes_only_the_owners_message()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Garbage]);
        var id = (await svc.ListUnparsedAsync(Asha)).Single().Id;

        Assert.Equal(TransactionActionResult.NotFound, await svc.DismissUnparsedAsync(Ravi, id));
        Assert.Single(await svc.ListUnparsedAsync(Asha));
        Assert.Equal(TransactionActionResult.Done, await svc.DismissUnparsedAsync(Asha, id));
        Assert.Empty(await svc.ListUnparsedAsync(Asha));
    }

    [Fact]
    public async Task Delete_removes_the_row_and_unflags_rows_that_pointed_at_it()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, SwiggyOtherMessageSameRef]);
        var rows = await svc.ListAsync(Asha, null);
        var original = rows.Single(t => t.DuplicateOfId is null);

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
