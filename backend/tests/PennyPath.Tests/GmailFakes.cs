using PennyPath.Application.Gmail;

namespace PennyPath.Tests;

/// <summary>A stand-in for Google: records what was asked and serves canned emails, so nothing here touches the network.</summary>
public sealed class FakeGoogleApi : IGoogleApi
{
    public bool IsConfigured { get; set; } = true;

    public Dictionary<string, EmailItem> Messages { get; } = new();
    public List<string> ReadIds { get; } = [];
    public List<string> Revoked { get; } = [];
    public string? LastQuery { get; private set; }
    public string? LastAccessToken { get; private set; }
    public (string Code, string Verifier)? LastExchange { get; private set; }
    public string? LastChallenge { get; private set; }

    /// <summary>What Google answers to the code exchange.</summary>
    public GoogleTokens NextTokens { get; set; } = new("1//refresh-secret-value", GmailQuery.ReadOnlyScope);
    public bool MoreAvailable { get; set; }
    public bool RefreshIsRevoked { get; set; }
    public Exception? SearchFails { get; set; }
    public Exception? RevokeFails { get; set; }

    /// <summary>When set, the search waits for this before answering (to test two syncs at once).</summary>
    public TaskCompletionSource? SearchGate { get; set; }

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        LastChallenge = codeChallenge;
        return $"https://google.test/auth?state={state}&challenge={codeChallenge}";
    }

    public Task<GoogleTokens> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        LastExchange = (code, codeVerifier);
        return Task.FromResult(NextTokens);
    }

    public Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (RefreshIsRevoked) throw new GoogleAuthException("invalid_grant");
        return Task.FromResult("access-for-" + refreshToken);
    }

    public async Task<(IReadOnlyList<string> Ids, bool More)> SearchMessageIdsAsync(string accessToken, string query, int max, CancellationToken ct)
    {
        LastAccessToken = accessToken;
        LastQuery = query;
        if (SearchGate is not null) await SearchGate.Task;
        if (SearchFails is not null) throw SearchFails;
        return (Messages.Keys.Take(max).ToList(), MoreAvailable);
    }

    public Task<EmailItem?> GetMessageAsync(string accessToken, string id, CancellationToken ct)
    {
        lock (ReadIds) ReadIds.Add(id);
        return Task.FromResult(Messages.TryGetValue(id, out var m) ? m : null);
    }

    public Task RevokeAsync(string token, CancellationToken ct)
    {
        Revoked.Add(token);
        return RevokeFails is null ? Task.CompletedTask : Task.FromException(RevokeFails);
    }

    /// <summary>The <c>state</c> the service put in the consent address.</summary>
    public static string StateOf(string url) => Param(url, "state");

    public static string Param(string url, string name) =>
        url[(url.IndexOf(name + "=", StringComparison.Ordinal) + name.Length + 1)..].Split('&')[0];
}

public static class Emails
{
    public const string Bank = "HDFC Bank <alerts@hdfcbank.net>";

    /// <summary>What Gmail writes for a message that really was signed by the bank.</summary>
    public static readonly string[] Auth =
    [
        "mx.google.com; dkim=pass header.i=@hdfcbank.net header.s=sel header.b=abc123; spf=pass smtp.mailfrom=hdfcbank.net; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from=hdfcbank.net",
    ];

    public static EmailItem Debit(string id, string merchant = "SWIGGY", string amount = "250.00") =>
        new(id, $"Alert: INR {amount} debited",
            $"Dear Customer, INR {amount} debited from A/c XX1234 on 02-11-26 to {merchant}. UPI Ref 4567890123. If you did not make this transaction, call us. Never share your OTP.",
            new DateOnly(2026, 11, 2), Bank, Auth);

    public static EmailItem Salary(string id) =>
        new(id, "Salary credited", "Rs. 45,000.00 credited to your A/c XX5678 on 01-11-2026 by ACME SALARY. Never share your OTP with anyone.", new DateOnly(2026, 11, 1), Bank, Auth);

    public static EmailItem Statement(string id) =>
        new(id, "Your October statement is ready", "Your monthly statement is ready to view. Thank you for banking with us.", new DateOnly(2026, 11, 3), Bank, Auth);

    public static EmailItem Otp(string id) =>
        new(id, "OTP for your transaction", "123456 is your OTP for the transaction. Do not share it.", new DateOnly(2026, 11, 3), Bank, Auth);
}
