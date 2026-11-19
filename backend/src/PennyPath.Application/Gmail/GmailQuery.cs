namespace PennyPath.Application.Gmail;

/// <summary>Builds the Gmail search so only likely bank alert emails are read (sender and wording), never the whole mailbox.</summary>
public static class GmailQuery
{
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/gmail.readonly";
    public const int FirstSyncDays = 90;

    /// <summary>Domains that send transaction alerts. Matching a domain also matches its subdomains.</summary>
    public static readonly string[] SenderDomains =
    [
        "hdfcbank.net", "hdfcbank.com", "icicibank.com", "axisbank.com", "sbi.co.in", "kotak.com", "kotak.bank.in",
        "yesbank.in", "idfcfirstbank.com", "indusind.com", "pnb.co.in", "bankofbaroda.com", "bankofbaroda.co.in",
        "canarabank.com", "unionbankofindia.co.in", "federalbank.co.in", "rblbank.com", "aubank.in", "bandhanbank.com",
        "citibank.com", "sc.com", "hsbc.co.in", "americanexpress.com", "paytm.com", "phonepe.com", "amazonpay.in",
    ];

    public static readonly string[] Keywords =
    [
        "debited", "credited", "spent", "withdrawn", "deposited", "refund", "UPI", "transaction",
    ];

    public static string Build(DateTime? lastSyncUtc, DateTime nowUtc)
    {
        // First sync: the last 90 days. Later syncs: since the last one, with a day of overlap for late-arriving mail.
        var from = lastSyncUtc is { } last && last > nowUtc.AddDays(-FirstSyncDays) ? last.AddDays(-1) : nowUtc.AddDays(-FirstSyncDays);
        var after = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var senders = string.Join(" OR ", SenderDomains.Select(d => $"from:{d}"));
        var words = string.Join(" OR ", Keywords);
        return $"after:{after} -in:spam -in:trash ({senders}) ({words})";
    }
}
