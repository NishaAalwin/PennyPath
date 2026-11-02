using PennyPath.Application.Parsing;
using PennyPath.Domain.Entities;

namespace PennyPath.Tests;

public class SmsTransactionParserTests
{
    private static readonly DateOnly Fallback = new(2026, 11, 2);
    private readonly SmsTransactionParser _p = new();

    [Theory]
    [InlineData("Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123", 250.00, TransactionType.Expense, "Swiggy", "1234", "2026-11-02")]
    [InlineData("Rs 450 spent on card XX9876 at AMAZON on 02-Nov-26. Avl bal Rs 12,000.00", 450, TransactionType.Expense, "Amazon", "9876", "2026-11-02")]
    [InlineData("Dear Customer, Rs. 15,000.00 credited to your A/c XX5678 on 01-11-2026 by salary", 15000, TransactionType.Income, "salary", "5678", "2026-11-01")]
    [InlineData("INR 1,299.00 debited from A/c XX4321 on 28-10-26 towards NETFLIX. Available balance INR 5,000", 1299, TransactionType.Expense, "Netflix", "4321", "2026-10-28")]
    [InlineData("Rs.500.00 credited to a/c **2222 on 30-10-26 from RAHUL SHARMA. UPI Ref 998877", 500, TransactionType.Income, "Rahul Sharma", "2222", "2026-10-30")]
    [InlineData("₹320 paid to VPA zomato@okicici on 25 Oct 2026", 320, TransactionType.Expense, "zomato", null, "2026-10-25")]
    public void Parses_common_bank_messages(string sms, double amount, TransactionType type, string? merchant, string? account, string date)
    {
        var r = _p.Parse(sms, Fallback);

        Assert.NotNull(r);
        Assert.Equal((decimal)amount, r.Amount);
        Assert.Equal(type, r.Type);
        Assert.Equal(merchant, r.Merchant);
        Assert.Equal(account, r.Account);
        Assert.Equal(DateOnly.Parse(date), r.Date);
    }

    [Fact]
    public void Ignores_the_available_balance_when_picking_the_amount()
    {
        var r = _p.Parse("Avl Bal Rs 99,999.00. Rs 100 debited from A/c XX1111 at CAFE on 02-11-26", Fallback);
        Assert.Equal(100m, r!.Amount);
    }

    [Fact]
    public void Uses_the_fallback_date_when_the_message_has_none()
    {
        var r = _p.Parse("Rs 75 debited from A/c XX1111 to UBER", Fallback);
        Assert.Equal(Fallback, r!.Date);
    }

    [Fact]
    public void First_direction_keyword_wins_for_debit_then_credit_wording()
    {
        var r = _p.Parse("Rs 600 debited from A/c XX1111 and credited to MEERA on 02-11-26", Fallback);
        Assert.Equal(TransactionType.Expense, r!.Type);
    }

    [Theory]
    [InlineData("123456 is your OTP for transaction of Rs 500. Do not share.")]
    [InlineData("Your payment of Rs 2,500 is due on 05-11-26. Min amount due Rs 250")]
    [InlineData("Rs 500 will be debited from your account on 05-11-26 for your SIP")]
    [InlineData("Transaction of Rs 900 declined due to insufficient funds")]
    [InlineData("Get a pre-approved loan of Rs 5,00,000. Apply now!")]
    [InlineData("Hello, see you at 5 tomorrow")]
    [InlineData("")]
    [InlineData("   ")]
    public void Returns_null_for_non_transactions(string sms) => Assert.Null(_p.Parse(sms, Fallback));
}
