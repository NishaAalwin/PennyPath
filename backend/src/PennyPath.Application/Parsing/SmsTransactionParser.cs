using System.Globalization;
using System.Text.RegularExpressions;
using PennyPath.Domain.Entities;

namespace PennyPath.Application.Parsing;

/// <summary>Rule-based parser for Indian bank / UPI / card alert messages (SMS or email text).</summary>
public partial class SmsTransactionParser : ITransactionParser
{
    private static readonly string[] Ignore =
    [
        "otp", "one time password", "will be debited", "will be credited", "has been requested", "requested money",
        "request from", "due on", "min amount due", "minimum amount due", "payment due", "failed", "declined",
        "not successful", "unsuccessful", "pre-approved", "offer", "apply now", "click",
    ];

    private static readonly string[] Expense = ["debited", "debit", "spent", "sent", "paid", "withdrawn", "purchase", "payment of"];
    private static readonly string[] Income = ["credited", "credit", "received", "deposited", "refund"];

    private static readonly string[] DateFormats =
    [
        "dd-MM-yy", "dd-MM-yyyy", "dd/MM/yy", "dd/MM/yyyy", "dd.MM.yy", "dd.MM.yyyy",
        "dd-MMM-yy", "dd-MMM-yyyy", "dd MMM yyyy", "dd MMM yy", "d-M-yy", "d/M/yy", "d-M-yyyy", "d/M/yyyy",
    ];

    public ParsedTransaction? Parse(string text, DateOnly fallbackDate)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = Whitespace().Replace(text.Trim(), " ");
        var lower = t.ToLowerInvariant();
        if (Ignore.Any(lower.Contains)) return null;

        var amount = FindAmount(t);
        var isRefund = RefundRx().IsMatch(lower);
        var type = isRefund ? TransactionType.Income : FindType(lower);
        if (amount is null || type is null) return null;

        var merchant = FindMerchant(t, type.Value);
        if (merchant is null && isRefund) merchant = FindMerchant(t, TransactionType.Expense); // "refund ... at SWIGGY"

        return new ParsedTransaction(
            amount.Value,
            type.Value,
            FindDate(t) ?? fallbackDate,
            merchant,
            FindAccount(t),
            FindReference(t),
            isRefund);
    }

    private static decimal? FindAmount(string t)
    {
        foreach (Match m in AmountRx().Matches(t))
        {
            var before = t[Math.Max(0, m.Index - 25)..m.Index].ToLowerInvariant();
            var sentenceBreak = before.LastIndexOf(". ", StringComparison.Ordinal);
            if (sentenceBreak >= 0) before = before[(sentenceBreak + 2)..];
            if (before.Contains("bal") || before.Contains("avl") || before.Contains("available") || before.Contains("limit"))
                continue;
            if (decimal.TryParse(m.Groups["amt"].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) && v > 0)
                return v;
        }
        return null;
    }

    private static TransactionType? FindType(string lower)
    {
        int Earliest(string[] words) => words
            .Select(w => Regex.Match(lower, $@"\b{Regex.Escape(w)}\b"))
            .Where(m => m.Success).Select(m => m.Index).DefaultIfEmpty(int.MaxValue).Min();

        var e = Earliest(Expense);
        var i = Earliest(Income);
        if (e == int.MaxValue && i == int.MaxValue) return null;
        return e < i ? TransactionType.Expense : TransactionType.Income;
    }

    private static DateOnly? FindDate(string t)
    {
        foreach (Match m in DateRx().Matches(t))
        {
            if (DateOnly.TryParseExact(m.Value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        }
        return null;
    }

    private static string? FindMerchant(string t, TransactionType type)
    {
        var rx = type == TransactionType.Expense ? PayeeRx() : PayerRx();
        var m = rx.Match(t);
        if (!m.Success) return null;
        var name = m.Groups["m"].Value.Trim(' ', '.', ',', '-');
        var at = name.IndexOf('@');
        if (at > 0) name = name[..at]; // UPI handle -> readable part
        if (name.Length < 2) return null;
        return name == name.ToUpperInvariant()
            ? CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant())
            : name;
    }

    private static string? FindReference(string t)
    {
        var m = ReferenceRx().Match(t);
        return m.Success ? m.Groups["r"].Value : null;
    }

    private static string? FindAccount(string t)
    {
        var m = AccountRx().Match(t);
        return m.Success ? m.Groups["n"].Value : null;
    }

    [GeneratedRegex(@"\b(?:refund(?:ed)?|reversal|reversed)\b")]
    private static partial Regex RefundRx();

    // "Ref 4567890123", "UPI Ref No. 998877", "UTR: AB123456789"; the value must contain a digit.
    [GeneratedRegex(@"\b(?:reference|ref|utr|rrn|txn)(?![A-Za-z])\s*(?:no\.?|number|id)?\s*[:#.\-]?\s*(?<r>(?=[A-Za-z0-9]*\d)[A-Za-z0-9]{6,30})", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenceRx();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?:rs\.?|inr|₹)\s*(?<amt>\d[\d,]*(?:\.\d{1,2})?)", RegexOptions.IgnoreCase)]
    private static partial Regex AmountRx();

    [GeneratedRegex(@"\b\d{1,2}[-/.]\d{1,2}[-/.]\d{2,4}\b|\b\d{1,2}[- ](?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*[- ]\d{2,4}\b", RegexOptions.IgnoreCase)]
    private static partial Regex DateRx();

    // "to SWIGGY on", "at AMAZON on", "towards NETFLIX", "to VPA swiggy@okaxis"
    [GeneratedRegex(@"\b(?:to|at|towards)\s+(?:vpa\s+)?(?!a/c|acct|account|your|card|the)(?<m>[A-Za-z0-9&@_][A-Za-z0-9&.@_ \-]{1,38}?)(?=\s+(?:on|ref|via|using|upi|avl|bal|info|txn|utr|from|if)\b|\s*[.,(]|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex PayeeRx();

    // "from RAHUL", "by salary", "by transfer from X"
    [GeneratedRegex(@"\b(?:from|by)\s+(?!a/c|acct|account|your|card|the)(?<m>[A-Za-z0-9&@_][A-Za-z0-9&.@_ \-]{1,38}?)(?=\s+(?:on|ref|via|using|upi|avl|bal|info|txn|utr|to)\b|\s*[.,(]|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex PayerRx();

    [GeneratedRegex(@"(?:a/c|acct|account|card)(?:\s*no\.?)?\s*(?:ending\s*(?:with\s*)?)?[xX*]*\s*(?<n>\d{3,6})", RegexOptions.IgnoreCase)]
    private static partial Regex AccountRx();
}
