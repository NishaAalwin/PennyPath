using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Parsing;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public class TransactionServiceTests
{
    private const string Swiggy = "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123";
    private const string Salary = "Rs. 15,000.00 credited to your A/c XX5678 on 01-11-2026 by salary";
    private const string Otp = "123456 is your OTP. Do not share.";

    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();

    private static TransactionService Create(AppDbContext db) =>
        new(db, new SmsTransactionParser(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)));

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Import_stores_parsed_messages_and_reports_unparsed()
    {
        using var db = NewDb();
        var svc = Create(db);

        var r = await svc.ImportSmsAsync(Asha, [Swiggy, Salary, Otp]);

        Assert.Equal(2, r.Imported);
        Assert.Equal(0, r.Duplicates);
        Assert.Single(r.Unparsed);
        var rows = await db.Transactions.ToListAsync();
        Assert.All(rows, t => Assert.Equal(TransactionSource.Sms, t.Source));
        Assert.All(rows, t => Assert.Equal(Asha, t.UserId));
    }

    [Fact]
    public async Task Reimporting_the_same_messages_creates_no_duplicates()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, Salary]);

        var r = await svc.ImportSmsAsync(Asha, [Swiggy.ToUpperInvariant(), "  " + Salary + "  "]);

        Assert.Equal(0, r.Imported);
        Assert.Equal(2, r.Duplicates);
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task The_same_message_twice_in_one_batch_is_stored_once()
    {
        using var db = NewDb();

        var r = await Create(db).ImportSmsAsync(Asha, [Swiggy, Swiggy]);

        Assert.Equal(1, r.Imported);
        Assert.Equal(1, r.Duplicates);
    }

    [Fact]
    public async Task Two_users_can_import_the_same_message_and_see_only_their_own()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy]);
        await svc.ImportSmsAsync(Ravi, [Swiggy, Salary]);

        Assert.Single(await svc.ListAsync(Asha, null));
        Assert.Equal(2, (await svc.ListAsync(Ravi, null)).Count);
    }

    [Fact]
    public async Task List_filters_by_month_and_orders_newest_first()
    {
        using var db = NewDb();
        var svc = Create(db);
        await svc.ImportSmsAsync(Asha, [Swiggy, Salary, "Rs 75 debited from A/c XX1111 to UBER on 28-10-26"]);

        var nov = await svc.ListAsync(Asha, "2026-11");
        var oct = await svc.ListAsync(Asha, "2026-10");

        Assert.Equal(2, nov.Count);
        Assert.True(nov[0].Date >= nov[1].Date);
        Assert.Single(oct);
        Assert.Equal("Uber", oct[0].Merchant);
    }

    [Fact]
    public async Task List_rejects_a_malformed_month()
    {
        using var db = NewDb();
        await Assert.ThrowsAsync<ArgumentException>(() => Create(db).ListAsync(Asha, "11-2026"));
    }
}
