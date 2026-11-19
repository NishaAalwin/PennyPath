using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Categorization;
using PennyPath.Application.Gmail;
using PennyPath.Application.Parsing;
using PennyPath.Application.Security;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Gmail;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Security;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

public sealed class GmailServiceTests : IDisposable
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();
    private const string Refresh = "1//refresh-secret-value";

    private readonly string _keys = Path.Combine(Path.GetTempPath(), "pp-gmail-keys-" + Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeGoogleApi _google = new();
    private readonly DataProtectionTokenProtector _protector;
    private readonly TransactionService _tx;
    private readonly GmailService _gmail;

    public GmailServiceTests()
    {
        _protector = new(DataProtectionProvider.Create(new DirectoryInfo(_keys), o => o.SetApplicationName("PennyPath")));
        _tx = new(_db, new SmsTransactionParser(), new RuleCategorizer(), _clock);
        _gmail = new(_db, _google, _protector, _tx, new AuditLog(_db, _clock), _clock, NullLogger<GmailService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_keys)) Directory.Delete(_keys, recursive: true);
    }

    private async Task<string> StartAsync(Guid user) => FakeGoogleApi.StateOf((await _gmail.StartConnectAsync(user))!);

    private async Task ConnectAsync(Guid user)
    {
        var state = await StartAsync(user);
        Assert.True(await _gmail.CompleteConnectAsync(state, "code-1", null, "127.0.0.1"));
    }

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---- connecting ----

    [Fact]
    public async Task Without_google_settings_nothing_starts_and_status_says_not_configured()
    {
        _google.IsConfigured = false;

        Assert.Null(await _gmail.StartConnectAsync(Asha));
        var status = await _gmail.StatusAsync(Asha);
        Assert.False(status.Configured);
        Assert.False(status.Connected);
        Assert.Equal(GmailSyncStatus.NotConfigured, (await _gmail.SyncAsync(Asha, null)).Status);
    }

    [Fact]
    public async Task Connecting_stores_only_the_encrypted_refresh_token()
    {
        await ConnectAsync(Asha);

        var c = await _db.GmailConnections.SingleAsync();
        Assert.Equal(Asha, c.UserId);
        Assert.DoesNotContain("refresh-secret", c.ProtectedRefreshToken);
        Assert.Equal(Refresh, _protector.Unprotect(c.ProtectedRefreshToken));
        Assert.True((await _gmail.StatusAsync(Asha)).Connected);
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.GmailConnected && e.UserId == Asha);
    }

    [Fact]
    public async Task The_pkce_challenge_matches_the_verifier_sent_with_the_code()
    {
        await ConnectAsync(Asha);

        var (code, verifier) = _google.LastExchange!.Value;
        Assert.Equal("code-1", code);
        Assert.Equal(Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), _google.LastChallenge);
    }

    [Fact]
    public async Task The_state_is_random_not_stored_in_the_clear_and_the_verifier_is_encrypted()
    {
        var s1 = await StartAsync(Asha);
        var s2 = await StartAsync(Ravi);

        Assert.NotEqual(s1, s2);
        Assert.True(s1.Length >= 40);
        var rows = await _db.GmailOAuthStates.ToListAsync();
        Assert.All(rows, r => Assert.DoesNotContain(s1, r.StateHash + r.ProtectedCodeVerifier));
        Assert.All(rows, r => Assert.NotNull(_protector.Unprotect(r.ProtectedCodeVerifier)));
    }

    [Fact]
    public async Task A_state_can_be_used_only_once()
    {
        var state = await StartAsync(Asha);

        Assert.True(await _gmail.CompleteConnectAsync(state, "code-1", null, null));
        Assert.False(await _gmail.CompleteConnectAsync(state, "code-1", null, null));
        Assert.Equal(1, await _db.GmailConnections.CountAsync());
        Assert.Equal(0, await _db.GmailOAuthStates.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-state")]
    public async Task Missing_or_unknown_state_connects_nothing(string? state)
    {
        await StartAsync(Asha);

        Assert.False(await _gmail.CompleteConnectAsync(state, "code-1", null, null));
        Assert.Equal(0, await _db.GmailConnections.CountAsync());
    }

    [Fact]
    public async Task An_expired_state_is_refused()
    {
        var state = await StartAsync(Asha);
        _clock.Advance(GmailService.StateLifetime + TimeSpan.FromSeconds(1));

        Assert.False(await _gmail.CompleteConnectAsync(state, "code-1", null, null));
        Assert.Equal(0, await _db.GmailConnections.CountAsync());
    }

    [Fact]
    public async Task Declining_on_googles_screen_connects_nothing_and_burns_the_state()
    {
        var state = await StartAsync(Asha);

        Assert.False(await _gmail.CompleteConnectAsync(state, null, "access_denied", null));
        Assert.Equal(0, await _db.GmailConnections.CountAsync());
        Assert.Equal(0, await _db.GmailOAuthStates.CountAsync());
    }

    [Fact]
    public async Task The_connection_belongs_to_the_user_who_started_it_not_to_whoever_arrives_with_the_code()
    {
        var state = await StartAsync(Asha);
        await StartAsync(Ravi);

        await _gmail.CompleteConnectAsync(state, "code-1", null, null);

        Assert.True((await _gmail.StatusAsync(Asha)).Connected);
        Assert.False((await _gmail.StatusAsync(Ravi)).Connected);
    }

    [Fact]
    public async Task A_grant_with_more_than_read_only_access_is_refused_and_revoked()
    {
        _google.NextTokens = new(Refresh, GmailQuery.ReadOnlyScope + " https://www.googleapis.com/auth/gmail.send");
        var state = await StartAsync(Asha);

        Assert.False(await _gmail.CompleteConnectAsync(state, "code-1", null, null));
        Assert.Equal(0, await _db.GmailConnections.CountAsync());
        Assert.Contains(Refresh, _google.Revoked);
    }

    [Fact]
    public async Task A_grant_without_a_refresh_token_is_refused()
    {
        _google.NextTokens = new(null, GmailQuery.ReadOnlyScope);
        var state = await StartAsync(Asha);

        Assert.False(await _gmail.CompleteConnectAsync(state, "code-1", null, null));
        Assert.Equal(0, await _db.GmailConnections.CountAsync());
    }

    [Fact]
    public async Task Connecting_again_replaces_the_old_grant_and_revokes_it()
    {
        await ConnectAsync(Asha);
        _google.NextTokens = new("1//second-token", GmailQuery.ReadOnlyScope);

        await ConnectAsync(Asha);

        var c = await _db.GmailConnections.SingleAsync();
        Assert.Equal("1//second-token", _protector.Unprotect(c.ProtectedRefreshToken));
        Assert.Contains(Refresh, _google.Revoked);
    }

    // ---- syncing ----

    [Fact]
    public async Task Sync_imports_alert_emails_and_reports_the_ones_it_could_not_read()
    {
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");
        _google.Messages["m2"] = Emails.Salary("m2");
        _google.Messages["m3"] = Emails.Statement("m3");
        _google.Messages["m4"] = Emails.Otp("m4");

        var o = await _gmail.SyncAsync(Asha, "127.0.0.1");

        Assert.Equal(GmailSyncStatus.Ok, o.Status);
        Assert.Equal(4, o.Result!.Checked);
        Assert.Equal(2, o.Result.Imported);
        Assert.Equal(2, o.Result.NotRecognised);
        Assert.Contains("Your October statement is ready", o.Result.NotRecognisedSubjects);
        var rows = await _db.Transactions.OrderBy(t => t.Amount).ToListAsync();
        Assert.Equal([250m, 45000m], rows.Select(t => t.Amount));
        Assert.All(rows, t => Assert.Equal(TransactionSource.Email, t.Source));
        Assert.All(rows, t => Assert.Equal(Asha, t.UserId));
        Assert.Equal("Food & Dining", rows[0].Category);
    }

    [Fact]
    public async Task An_email_that_only_claims_to_be_from_the_bank_is_not_imported()
    {
        await ConnectAsync(Asha);
        _google.Messages["real"] = Emails.Debit("real");
        // A forged alert: right look and sender address, but Gmail could not verify the signature.
        _google.Messages["fake"] = Emails.Debit("fake", "CASINO", "99999.00") with { AuthenticationResults = ["mx.google.com; dkim=fail header.i=@hdfcbank.net; spf=softfail smtp.mailfrom=evil.example"] };
        _google.Messages["none"] = Emails.Debit("none", "SCAM", "88888.00") with { AuthenticationResults = null };
        _google.Messages["other"] = Emails.Debit("other", "PHISH", "77777.00") with { From = "Support <help@hdfcbank.net.evil.example>" };

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.Equal(1, o.Result!.Imported);
        Assert.Equal(3, o.Result.UnverifiedSender);
        var row = await _db.Transactions.SingleAsync();
        Assert.Equal(250m, row.Amount);
    }

    [Fact]
    public async Task Sync_keeps_no_email_text_only_the_parsed_fields_and_a_key_derived_from_the_message_id()
    {
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");

        await _gmail.SyncAsync(Asha, null);

        var t = await _db.Transactions.SingleAsync();
        Assert.Equal(TransactionService.EmailKey("m1"), t.RawHash);
        var everything = string.Join(" ", t.Merchant, t.Category, t.RawHash);
        Assert.DoesNotContain("4567890123", everything);   // reference number
        Assert.DoesNotContain("XX1234", everything);       // account
        Assert.DoesNotContain("Dear Customer", everything);
        Assert.Equal(64, t.RawHash.Length); // a SHA-256 hex digest, not the message id
    }

    [Fact]
    public async Task Syncing_twice_creates_no_duplicates_and_does_not_read_known_emails_again()
    {
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");
        _google.Messages["m2"] = Emails.Salary("m2");
        await _gmail.SyncAsync(Asha, null);
        _google.ReadIds.Clear();

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.Equal(0, o.Result!.Imported);
        Assert.Equal(2, o.Result.Duplicates);
        Assert.Equal(0, o.Result.Checked);
        Assert.Empty(_google.ReadIds);
        Assert.Equal(2, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task A_new_email_arriving_later_is_picked_up_by_the_next_sync()
    {
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");
        await _gmail.SyncAsync(Asha, null);
        _google.Messages["m2"] = Emails.Debit("m2", "UBER", "480.00");

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.Equal(1, o.Result!.Imported);
        Assert.Equal(2, await _db.Transactions.CountAsync());
    }

    [Fact]
    public async Task The_search_is_limited_to_bank_senders_and_alert_words_for_the_last_90_days_at_first()
    {
        await ConnectAsync(Asha);

        await _gmail.SyncAsync(Asha, null);

        var q = _google.LastQuery!;
        Assert.Contains("from:hdfcbank.net", q);
        Assert.Contains("from:icicibank.com", q);
        Assert.Contains("debited", q);
        Assert.Contains("-in:spam", q);
        var after = long.Parse(q.Split("after:")[1].Split(' ')[0]);
        var expected = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero).AddDays(-90).ToUnixTimeSeconds();
        Assert.Equal(expected, after);
        Assert.Equal("access-for-" + Refresh, _google.LastAccessToken);
    }

    [Fact]
    public async Task Later_syncs_start_from_the_last_one_with_a_day_of_overlap()
    {
        await ConnectAsync(Asha);
        await _gmail.SyncAsync(Asha, null);
        _clock.Advance(TimeSpan.FromDays(3));

        await _gmail.SyncAsync(Asha, null);

        var after = long.Parse(_google.LastQuery!.Split("after:")[1].Split(' ')[0]);
        var firstSync = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        Assert.Equal(firstSync.AddDays(-1).ToUnixTimeSeconds(), after);
    }

    [Fact]
    public async Task When_more_emails_exist_than_one_sync_reads_the_marker_does_not_advance()
    {
        await ConnectAsync(Asha);
        _google.MoreAvailable = true;
        _google.Messages["m1"] = Emails.Debit("m1");

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.True(o.Result!.MoreAvailable);
        Assert.Null((await _gmail.StatusAsync(Asha)).LastSyncAt);
    }

    [Fact]
    public async Task A_payment_seen_by_sms_and_by_email_is_flagged_as_a_possible_duplicate()
    {
        await _tx.ImportSmsAsync(Asha, ["Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26 Ref 4567890123"]);
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.Equal(1, o.Result!.Imported);
        Assert.Equal(1, o.Result.PossibleDuplicates);
        Assert.Equal(1, await _db.Transactions.CountAsync(t => t.DuplicateOfId != null));
    }

    [Fact]
    public async Task Sync_without_a_connection_does_nothing()
    {
        Assert.Equal(GmailSyncStatus.NotConnected, (await _gmail.SyncAsync(Asha, null)).Status);
        Assert.Null(_google.LastQuery);
    }

    [Fact]
    public async Task One_users_connection_is_not_usable_by_another_user()
    {
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");

        Assert.Equal(GmailSyncStatus.NotConnected, (await _gmail.SyncAsync(Ravi, null)).Status);
        Assert.False((await _gmail.StatusAsync(Ravi)).Connected);

        await _gmail.SyncAsync(Asha, null);
        Assert.All(await _db.Transactions.ToListAsync(), t => Assert.Equal(Asha, t.UserId));
    }

    [Fact]
    public async Task A_revoked_or_expired_grant_asks_the_user_to_reconnect_and_forgets_the_token()
    {
        await ConnectAsync(Asha);
        _google.RefreshIsRevoked = true;

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.Equal(GmailSyncStatus.NeedsReconnect, o.Status);
        Assert.Equal(0, await _db.GmailConnections.CountAsync());
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.GmailNeedsReconnect);
    }

    [Fact]
    public async Task If_the_key_ring_is_lost_the_token_cannot_be_read_and_the_user_must_reconnect()
    {
        await ConnectAsync(Asha);
        (await _db.GmailConnections.SingleAsync()).ProtectedRefreshToken = "damaged";
        await _db.SaveChangesAsync();

        Assert.Equal(GmailSyncStatus.NeedsReconnect, (await _gmail.SyncAsync(Asha, null)).Status);
    }

    [Fact]
    public async Task A_network_failure_is_reported_without_details_and_keeps_the_connection()
    {
        await ConnectAsync(Asha);
        _google.SearchFails = new HttpRequestException("connection reset to https://secret-host/?token=abc");

        var o = await _gmail.SyncAsync(Asha, null);

        Assert.Equal(GmailSyncStatus.Failed, o.Status);
        Assert.Null(o.Result);
        Assert.Equal(1, await _db.GmailConnections.CountAsync());
    }

    [Fact]
    public async Task Two_syncs_at_once_run_one_at_a_time()
    {
        await ConnectAsync(Asha);
        _google.SearchGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = _gmail.SyncAsync(Asha, null);
        await Task.Delay(100);

        var second = await _gmail.SyncAsync(Asha, null);
        _google.SearchGate.SetResult();

        Assert.Equal(GmailSyncStatus.Busy, second.Status);
        Assert.Equal(GmailSyncStatus.Ok, (await first).Status);
    }

    [Fact]
    public async Task Sync_writes_an_audit_event_with_no_email_content()
    {
        await ConnectAsync(Asha);
        _google.Messages["m1"] = Emails.Debit("m1");

        await _gmail.SyncAsync(Asha, "10.0.0.1");

        var e = Assert.Single(await _db.AuditEvents.Where(x => x.Type == AuditEventTypes.GmailSynced).ToListAsync());
        Assert.Equal(Asha, e.UserId);
        Assert.Equal("10.0.0.1", e.Ip);
    }

    // ---- disconnecting ----

    [Fact]
    public async Task Disconnect_deletes_the_stored_token_and_revokes_it_at_google()
    {
        await ConnectAsync(Asha);

        await _gmail.DisconnectAsync(Asha, null);

        Assert.Equal(0, await _db.GmailConnections.CountAsync());
        Assert.Equal([Refresh], _google.Revoked);
        Assert.False((await _gmail.StatusAsync(Asha)).Connected);
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.GmailDisconnected);
    }

    [Fact]
    public async Task Disconnect_still_deletes_the_token_when_google_cannot_be_reached()
    {
        await ConnectAsync(Asha);
        _google.RevokeFails = new HttpRequestException("offline");

        await _gmail.DisconnectAsync(Asha, null);

        Assert.Equal(0, await _db.GmailConnections.CountAsync());
    }

    [Fact]
    public async Task Disconnect_leaves_other_users_and_already_imported_transactions_alone()
    {
        await ConnectAsync(Asha);
        await ConnectAsync(Ravi);
        _google.Messages["m1"] = Emails.Debit("m1");
        await _gmail.SyncAsync(Asha, null);

        await _gmail.DisconnectAsync(Asha, null);

        Assert.True((await _gmail.StatusAsync(Ravi)).Connected);
        Assert.Equal(1, await _db.Transactions.CountAsync(t => t.UserId == Asha));
    }

    [Fact]
    public async Task Disconnecting_when_not_connected_is_harmless()
    {
        await _gmail.DisconnectAsync(Asha, null);

        Assert.Empty(_google.Revoked);
    }
}
