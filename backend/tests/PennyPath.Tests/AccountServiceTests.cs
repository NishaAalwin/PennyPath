using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Account;
using PennyPath.Application.Auth;
using PennyPath.Application.Categorization;
using PennyPath.Application.Gmail;
using PennyPath.Application.Parsing;
using PennyPath.Application.Security;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Account;
using PennyPath.Infrastructure.Auth;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Security;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Tests;

internal sealed class StubGmail : IGmailService
{
    public List<Guid> Disconnected { get; } = [];
    public Task<GmailStatus> StatusAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<string?> StartConnectAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> CompleteConnectAsync(string? state, string? code, string? error, string? ip, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<GmailSyncOutcome> SyncAsync(Guid userId, string? ip, CancellationToken ct = default) => throw new NotSupportedException();

    public Task DisconnectAsync(Guid userId, string? ip, CancellationToken ct = default)
    {
        Disconnected.Add(userId);
        return Task.CompletedTask;
    }
}

public sealed class AccountServiceTests : IDisposable
{
    private const string Password = "correct-horse-battery";
    private const string NewPassword = "a-different-long-passphrase";

    private readonly string _keys = Path.Combine(Path.GetTempPath(), "pp-acct-keys-" + Guid.NewGuid().ToString("N"));
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeEmailSender _mail = new();
    private readonly StubGmail _gmail = new();
    private readonly DataProtectionTokenProtector _protector;
    private readonly AuthService _auth;
    private readonly AccountService _account;
    private readonly TransactionService _tx;

    public AccountServiceTests()
    {
        _protector = new(DataProtectionProvider.Create(new DirectoryInfo(_keys), o => o.SetApplicationName("PennyPath")));
        var mfa = new MfaVerifier(_db, _protector, _clock);
        var tokens = new JwtTokenService(Options.Create(new JwtOptions { Key = new string('k', 40) }));
        _auth = new AuthService(_db, tokens, _clock, mfa, Options.Create(new AuthOptions()));
        _tx = new TransactionService(_db, new SmsTransactionParser(), new RuleCategorizer(), _clock);
        _account = new AccountService(_db, _mail, _auth, _gmail, _protector, _tx, new AuditLog(_db, _clock), _clock,
            Options.Create(new AppOptions { PublicUrl = "https://pennypath.test/" }));
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_keys)) Directory.Delete(_keys, recursive: true);
    }

    private async Task<Guid> SignUpAsync(string email)
    {
        var r = await _auth.RegisterAsync(new RegisterRequest(email, Password));
        Assert.True(r.Succeeded);
        return r.UserId!.Value;
    }

    private string CodeNow(string base32, int stepOffset = 0) =>
        Totp.CodeFor(Totp.FromBase32(base32)!, Totp.StepOf(_clock.GetUtcNow()) + stepOffset);

    /// <summary>Turns two-factor on for a user and returns the secret and the recovery codes.</summary>
    private async Task<(string Secret, IReadOnlyList<string> Codes)> EnableMfaAsync(Guid user)
    {
        var setup = (await _account.BeginMfaSetupAsync(user))!;
        var r = await _account.EnableMfaAsync(user, CodeNow(setup.Secret), null);
        Assert.True(r.Enabled);
        _clock.Advance(TimeSpan.FromSeconds(Totp.StepSeconds * 2)); // a later code than the one used to enable
        return (setup.Secret, r.RecoveryCodes);
    }

    // ---- email verification ----

    [Fact]
    public async Task New_accounts_start_unverified_and_get_one_email_with_a_link_after_the_hash_sign()
    {
        var id = await SignUpAsync("asha@example.com");
        await _account.SendVerificationAsync(id);

        Assert.False((await _db.Users.SingleAsync()).EmailVerified);
        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("asha@example.com", mail.To);
        Assert.Contains("https://pennypath.test/#verify=", mail.Body);
        Assert.DoesNotContain("?", mail.Body.Split("#verify=")[1].Split('\n')[0]); // the token is in the fragment, never a query
    }

    [Fact]
    public async Task The_link_token_is_stored_only_as_a_hash()
    {
        var id = await SignUpAsync("asha@example.com");
        await _account.SendVerificationAsync(id);
        var token = _mail.LastToken("asha@example.com", "verify")!;

        var row = await _db.UserTokens.SingleAsync();
        Assert.DoesNotContain(token, row.TokenHash);
        Assert.Equal(64, row.TokenHash.Length);
        Assert.True(token.Length >= 40);
    }

    [Fact]
    public async Task Opening_the_link_verifies_the_account_once()
    {
        var id = await SignUpAsync("asha@example.com");
        await _account.SendVerificationAsync(id);
        var token = _mail.LastToken("asha@example.com", "verify")!;

        Assert.True(await _account.VerifyEmailAsync(token, "127.0.0.1"));
        Assert.True((await _db.Users.SingleAsync()).EmailVerified);
        Assert.False(await _account.VerifyEmailAsync(token, null)); // a used link never works again
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.EmailVerified && e.UserId == id);
    }

    [Fact]
    public async Task A_verification_link_expires_after_a_day()
    {
        var id = await SignUpAsync("asha@example.com");
        await _account.SendVerificationAsync(id);
        var token = _mail.LastToken("asha@example.com", "verify")!;
        _clock.Advance(AccountService.VerifyLifetime + TimeSpan.FromMinutes(1));

        Assert.False(await _account.VerifyEmailAsync(token, null));
        Assert.False((await _db.Users.SingleAsync()).EmailVerified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Missing_or_unknown_tokens_verify_nothing(string? token)
    {
        await SignUpAsync("asha@example.com");

        Assert.False(await _account.VerifyEmailAsync(token, null));
    }

    [Fact]
    public async Task A_newer_link_voids_the_older_one()
    {
        var id = await SignUpAsync("asha@example.com");
        await _account.SendVerificationAsync(id);
        var first = _mail.LastToken("asha@example.com", "verify")!;
        await _account.SendVerificationAsync(id);
        var second = _mail.LastToken("asha@example.com", "verify")!;

        Assert.NotEqual(first, second);
        Assert.False(await _account.VerifyEmailAsync(first, null));
        Assert.True(await _account.VerifyEmailAsync(second, null));
    }

    [Fact]
    public async Task Asking_for_links_over_and_over_sends_at_most_three_an_hour()
    {
        var id = await SignUpAsync("asha@example.com");
        for (var i = 0; i < 6; i++) await _account.SendVerificationAsync(id);

        Assert.Equal(AccountService.MaxLinksPerHour, _mail.Sent.Count);
        _clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(1));
        await _account.SendVerificationAsync(id);
        Assert.Equal(AccountService.MaxLinksPerHour + 1, _mail.Sent.Count);
    }

    [Fact]
    public async Task A_verified_account_gets_no_more_verification_mail()
    {
        var id = await SignUpAsync("asha@example.com");
        (await _db.Users.SingleAsync()).EmailVerified = true;
        await _db.SaveChangesAsync();

        await _account.SendVerificationAsync(id);

        Assert.Empty(_mail.Sent);
    }

    // ---- password reset ----

    [Fact]
    public async Task Asking_to_reset_an_unknown_address_sends_nothing_and_says_nothing()
    {
        await SignUpAsync("asha@example.com");

        await _account.RequestPasswordResetAsync("nobody@example.com", null);
        await _account.RequestPasswordResetAsync("", null);
        await _account.RequestPasswordResetAsync(new string('a', 400), null);

        Assert.Empty(_mail.Sent);
        Assert.Empty(await _db.UserTokens.ToListAsync());
    }

    [Fact]
    public async Task A_known_address_gets_a_reset_link_that_expires_in_half_an_hour()
    {
        await SignUpAsync("asha@example.com");

        await _account.RequestPasswordResetAsync("  ASHA@example.com ", "127.0.0.1");

        var mail = Assert.Single(_mail.Sent);
        Assert.Contains("https://pennypath.test/#reset=", mail.Body);
        Assert.Contains("30 minutes", mail.Body);
        Assert.Equal(UserTokenPurpose.ResetPassword, (await _db.UserTokens.SingleAsync()).Purpose);
    }

    [Fact]
    public async Task A_reset_sets_the_new_password_signs_out_every_session_and_clears_the_lockout()
    {
        var id = await SignUpAsync("asha@example.com");
        var session = await _auth.LoginAsync(new LoginRequest("asha@example.com", Password));
        Assert.True(session.Succeeded);
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++) await _auth.LoginAsync(new LoginRequest("asha@example.com", "wrong-password-1"));
        Assert.NotNull((await _db.Users.SingleAsync()).LockoutEnd);

        await _account.RequestPasswordResetAsync("asha@example.com", null);
        var r = await _account.ResetPasswordAsync(_mail.LastToken("asha@example.com", "reset"), NewPassword, null);

        Assert.Equal(ResetOutcome.Done, r.Outcome);
        Assert.All(await _db.Sessions.Where(s => s.UserId == id).ToListAsync(), s => Assert.NotNull(s.RevokedAt));
        Assert.Null((await _db.Users.SingleAsync()).LockoutEnd);
        Assert.False((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password))).Succeeded);
        Assert.True((await _auth.LoginAsync(new LoginRequest("asha@example.com", NewPassword))).Succeeded);
    }

    [Fact]
    public async Task A_reset_link_works_once()
    {
        await SignUpAsync("asha@example.com");
        await _account.RequestPasswordResetAsync("asha@example.com", null);
        var token = _mail.LastToken("asha@example.com", "reset");

        Assert.Equal(ResetOutcome.Done, (await _account.ResetPasswordAsync(token, NewPassword, null)).Outcome);
        Assert.Equal(ResetOutcome.InvalidToken, (await _account.ResetPasswordAsync(token, "yet-another-passphrase-1", null)).Outcome);
        Assert.True((await _auth.LoginAsync(new LoginRequest("asha@example.com", NewPassword))).Succeeded);
    }

    [Fact]
    public async Task A_reset_link_expires()
    {
        await SignUpAsync("asha@example.com");
        await _account.RequestPasswordResetAsync("asha@example.com", null);
        var token = _mail.LastToken("asha@example.com", "reset");
        _clock.Advance(AccountService.ResetLifetime + TimeSpan.FromSeconds(1));

        Assert.Equal(ResetOutcome.InvalidToken, (await _account.ResetPasswordAsync(token, NewPassword, null)).Outcome);
        Assert.True((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password))).Succeeded);
    }

    [Fact]
    public async Task A_verification_link_cannot_be_used_to_reset_a_password_nor_the_other_way_round()
    {
        var id = await SignUpAsync("asha@example.com");
        await _account.SendVerificationAsync(id);
        await _account.RequestPasswordResetAsync("asha@example.com", null);

        Assert.Equal(ResetOutcome.InvalidToken, (await _account.ResetPasswordAsync(_mail.LastToken("asha@example.com", "verify"), NewPassword, null)).Outcome);
        Assert.False(await _account.VerifyEmailAsync(_mail.LastToken("asha@example.com", "reset"), null));
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_and_the_link_stays_usable()
    {
        await SignUpAsync("asha@example.com");
        await _account.RequestPasswordResetAsync("asha@example.com", null);
        var token = _mail.LastToken("asha@example.com", "reset");

        Assert.Equal(ResetOutcome.WeakPassword, (await _account.ResetPasswordAsync(token, "short", null)).Outcome);
        Assert.Equal(ResetOutcome.WeakPassword, (await _account.ResetPasswordAsync(token, "password123", null)).Outcome);
        Assert.Equal(ResetOutcome.Done, (await _account.ResetPasswordAsync(token, NewPassword, null)).Outcome);
    }

    [Fact]
    public async Task A_second_reset_request_voids_the_first_link()
    {
        await SignUpAsync("asha@example.com");
        await _account.RequestPasswordResetAsync("asha@example.com", null);
        var first = _mail.LastToken("asha@example.com", "reset");
        await _account.RequestPasswordResetAsync("asha@example.com", null);

        Assert.Equal(ResetOutcome.InvalidToken, (await _account.ResetPasswordAsync(first, NewPassword, null)).Outcome);
        Assert.Equal(ResetOutcome.Done, (await _account.ResetPasswordAsync(_mail.LastToken("asha@example.com", "reset"), NewPassword, null)).Outcome);
    }

    [Fact]
    public async Task Reset_requests_are_throttled_per_account()
    {
        await SignUpAsync("asha@example.com");
        for (var i = 0; i < 8; i++) await _account.RequestPasswordResetAsync("asha@example.com", null);

        Assert.Equal(AccountService.MaxLinksPerHour, _mail.Sent.Count);
    }

    [Fact]
    public async Task A_reset_also_verifies_the_address_because_the_user_proved_they_read_its_mail()
    {
        await SignUpAsync("asha@example.com");
        await _account.RequestPasswordResetAsync("asha@example.com", null);

        await _account.ResetPasswordAsync(_mail.LastToken("asha@example.com", "reset"), NewPassword, null);

        Assert.True((await _db.Users.SingleAsync()).EmailVerified);
    }

    [Fact]
    public async Task A_reset_does_not_switch_two_factor_off()
    {
        var id = await SignUpAsync("asha@example.com");
        await EnableMfaAsync(id);

        await _account.RequestPasswordResetAsync("asha@example.com", null);
        await _account.ResetPasswordAsync(_mail.LastToken("asha@example.com", "reset"), NewPassword, null);

        Assert.True((await _db.Users.SingleAsync()).MfaEnabled);
        Assert.Equal(AuthError.MfaRequired, (await _auth.LoginAsync(new LoginRequest("asha@example.com", NewPassword))).Error);
    }

    // ---- deleting the account ----

    private async Task SeedDataAsync(Guid user)
    {
        await _tx.ImportSmsAsync(user, ["Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26", "Rs. 15,000.00 credited to your A/c XX5678 on 01-11-2026 by salary"]);
        _db.GmailConnections.Add(new GmailConnection { UserId = user, ProtectedRefreshToken = _protector.Protect("1//secret"), ConnectedAt = _clock.GetUtcNow().UtcDateTime });
        _db.GmailOAuthStates.Add(new GmailOAuthState { UserId = user, StateHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), ProtectedCodeVerifier = "x", ExpiresAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(5) });
        await _db.SaveChangesAsync();
        await _auth.LoginAsync(new LoginRequest((await _db.Users.FirstAsync(u => u.Id == user)).Email, Password));
        await _account.SendVerificationAsync(user);
        await new AiConsentService(_db, _clock).SetAsync(user, true);
    }

    private async Task<int> RowsOfAsync(Guid user) =>
        await _db.Users.CountAsync(u => u.Id == user) + await _db.Transactions.CountAsync(t => t.UserId == user)
        + await _db.Sessions.CountAsync(s => s.UserId == user) + await _db.AuditEvents.CountAsync(e => e.UserId == user)
        + await _db.GmailConnections.CountAsync(g => g.UserId == user) + await _db.GmailOAuthStates.CountAsync(g => g.UserId == user)
        + await _db.UserTokens.CountAsync(t => t.UserId == user) + await _db.RecoveryCodes.CountAsync(r => r.UserId == user);

    [Fact]
    public async Task Deleting_the_account_removes_every_row_that_belongs_to_it_and_nothing_else()
    {
        var asha = await SignUpAsync("asha@example.com");
        var ravi = await SignUpAsync("ravi@example.com");
        await SeedDataAsync(asha);
        await SeedDataAsync(ravi);
        await EnableMfaAsync(asha);
        var ravisRows = await RowsOfAsync(ravi);
        Assert.True(await RowsOfAsync(asha) > 8);

        var code = (await _db.Users.AsNoTracking().FirstAsync(u => u.Id == asha)).ProtectedMfaSecret!;
        var secret = _protector.Unprotect(code)!;
        var outcome = await _account.DeleteAccountAsync(asha, Password, CodeNow(secret), "127.0.0.1");

        Assert.Equal(DeleteOutcome.Deleted, outcome);
        Assert.Equal(0, await RowsOfAsync(asha));
        Assert.Equal(ravisRows, await RowsOfAsync(ravi));
        Assert.Equal([asha], _gmail.Disconnected);
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.AccountDeleted && e.UserId == null);
    }

    [Fact]
    public async Task A_deleted_account_cannot_sign_in_or_be_reset()
    {
        var id = await SignUpAsync("asha@example.com");

        await _account.DeleteAccountAsync(id, Password, null, null);

        Assert.False((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password))).Succeeded);
        await _account.RequestPasswordResetAsync("asha@example.com", null);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task Deleting_needs_the_right_password_and_a_wrong_one_deletes_nothing()
    {
        var id = await SignUpAsync("asha@example.com");
        await SeedDataAsync(id);
        var before = await RowsOfAsync(id);

        Assert.Equal(DeleteOutcome.NotAuthenticated, await _account.DeleteAccountAsync(id, "wrong-password-1", null, null));
        Assert.Equal(DeleteOutcome.NotAuthenticated, await _account.DeleteAccountAsync(id, null, null, null));
        Assert.Equal(DeleteOutcome.NotAuthenticated, await _account.DeleteAccountAsync(id, "", null, null));

        Assert.True(await RowsOfAsync(id) >= before);
        Assert.Empty(_gmail.Disconnected);
    }

    [Fact]
    public async Task Wrong_passwords_while_deleting_lock_the_account_like_a_sign_in()
    {
        var id = await SignUpAsync("asha@example.com");
        for (var i = 0; i < AuthService.MaxFailedAttempts; i++) await _account.DeleteAccountAsync(id, "wrong-password-1", null, null);

        Assert.Equal(DeleteOutcome.LockedOut, await _account.DeleteAccountAsync(id, Password, null, null));
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Fact]
    public async Task With_two_factor_on_deleting_also_needs_a_code()
    {
        var id = await SignUpAsync("asha@example.com");
        var (secret, _) = await EnableMfaAsync(id);

        Assert.Equal(DeleteOutcome.MfaRequired, await _account.DeleteAccountAsync(id, Password, null, null));
        Assert.Equal(DeleteOutcome.NotAuthenticated, await _account.DeleteAccountAsync(id, Password, "000000", null));
        Assert.Equal(1, await _db.Users.CountAsync());
        Assert.Equal(DeleteOutcome.Deleted, await _account.DeleteAccountAsync(id, Password, CodeNow(secret), null));
    }

    [Fact]
    public async Task One_users_password_does_not_unlock_deleting_another_account()
    {
        await SignUpAsync("asha@example.com");
        var ravi = await SignUpAsync("ravi@example.com");
        var ravi_user = await _db.Users.FirstAsync(u => u.Id == ravi);
        ravi_user.PasswordHash = AuthService.HashPassword(ravi_user, "ravis-own-password-1");
        await _db.SaveChangesAsync();

        // Asha's password is not Ravi's.
        Assert.Equal(DeleteOutcome.NotAuthenticated, await _account.DeleteAccountAsync(ravi, Password, null, null));
        Assert.Equal(2, await _db.Users.CountAsync());
    }

    // ---- export ----

    [Fact]
    public async Task The_export_holds_the_users_own_data_and_no_secret_of_any_kind()
    {
        var asha = await SignUpAsync("asha@example.com");
        var ravi = await SignUpAsync("ravi@example.com");
        await SeedDataAsync(asha);
        await _tx.ImportSmsAsync(ravi, ["Sent Rs.999.00 From HDFC Bank A/c *1234 To ZOMATO On 02/11/26"]);
        await EnableMfaAsync(asha);

        var export = (await _account.ExportAsync(asha, "127.0.0.1"))!;
        var json = JsonSerializer.Serialize(export);

        Assert.Equal("asha@example.com", export.Email);
        Assert.Equal(2, export.Transactions.Count);
        Assert.True(export.MfaEnabled && export.GmailConnected && export.AiConsent);
        Assert.DoesNotContain("Zomato", json, StringComparison.OrdinalIgnoreCase); // another user data
        Assert.DoesNotContain("ravi@example.com", json);
        var user = await _db.Users.AsNoTracking().FirstAsync(u => u.Id == asha);
        Assert.DoesNotContain(user.PasswordHash, json);
        Assert.DoesNotContain(user.ProtectedMfaSecret!, json);
        foreach (var bad in new[] { "PasswordHash", "ProtectedMfaSecret", "ProtectedRefreshToken", "TokenHash", "CodeHash", "RawHash", "1//secret" })
            Assert.DoesNotContain(bad, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.DataExported && e.UserId == asha);
    }

    [Fact]
    public async Task Exporting_an_unknown_user_gives_nothing()
    {
        Assert.Null(await _account.ExportAsync(Guid.NewGuid(), null));
    }

    // ---- two-factor login ----

    [Fact]
    public async Task Setup_gives_a_secret_and_an_authenticator_address_and_stores_the_secret_encrypted()
    {
        var id = await SignUpAsync("asha@example.com");

        var setup = (await _account.BeginMfaSetupAsync(id))!;

        Assert.Matches("^[A-Z2-7]{32}$", setup.Secret);
        Assert.StartsWith("otpauth://totp/PennyPath:asha%40example.com", setup.OtpAuthUri);
        var user = await _db.Users.SingleAsync();
        Assert.False(user.MfaEnabled); // not on until a code proves the app has the secret
        Assert.DoesNotContain(setup.Secret, user.ProtectedMfaSecret!);
        Assert.Equal(setup.Secret, _protector.Unprotect(user.ProtectedMfaSecret!));
    }

    [Fact]
    public async Task Two_factor_turns_on_only_with_a_right_code_and_then_gives_ten_recovery_codes()
    {
        var id = await SignUpAsync("asha@example.com");
        var setup = (await _account.BeginMfaSetupAsync(id))!;

        Assert.False((await _account.EnableMfaAsync(id, "000000", null)).Enabled);
        Assert.False((await _account.EnableMfaAsync(id, null, null)).Enabled);
        Assert.False((await _db.Users.SingleAsync()).MfaEnabled);

        var r = await _account.EnableMfaAsync(id, CodeNow(setup.Secret), "127.0.0.1");

        Assert.True(r.Enabled);
        Assert.Equal(10, r.RecoveryCodes.Count);
        Assert.True((await _db.Users.SingleAsync()).MfaEnabled);
        var stored = await _db.RecoveryCodes.ToListAsync();
        Assert.Equal(10, stored.Count);
        Assert.All(r.RecoveryCodes, c => Assert.DoesNotContain(stored, s => s.CodeHash.Contains(c.Replace("-", ""))));
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.MfaEnabled);
        Assert.Null(await _account.BeginMfaSetupAsync(id)); // already on
    }

    [Fact]
    public async Task With_two_factor_on_the_password_alone_gets_no_session()
    {
        var id = await SignUpAsync("asha@example.com");
        await EnableMfaAsync(id);
        var sessionsBefore = await _db.Sessions.CountAsync();

        var r = await _auth.LoginAsync(new LoginRequest("asha@example.com", Password));

        Assert.Equal(AuthError.MfaRequired, r.Error);
        Assert.Null(r.Response);
        Assert.Equal(sessionsBefore, await _db.Sessions.CountAsync());
    }

    [Fact]
    public async Task Password_and_a_right_code_sign_in_and_the_same_code_cannot_be_replayed()
    {
        var id = await SignUpAsync("asha@example.com");
        var (secret, _) = await EnableMfaAsync(id);
        var code = CodeNow(secret);

        var first = await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, code));
        var replay = await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, code));

        Assert.True(first.Succeeded);
        Assert.False(replay.Succeeded);
        Assert.Equal(AuthError.InvalidCredentials, replay.Error);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_a_wrong_password_with_a_right_code_too()
    {
        var id = await SignUpAsync("asha@example.com");
        var (secret, _) = await EnableMfaAsync(id);

        Assert.False((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, "000000"))).Succeeded);
        Assert.False((await _auth.LoginAsync(new LoginRequest("asha@example.com", "wrong-password-1", CodeNow(secret)))).Succeeded);
    }

    [Fact]
    public async Task Guessing_codes_with_the_right_password_runs_into_the_lockout()
    {
        var id = await SignUpAsync("asha@example.com");
        var (secret, _) = await EnableMfaAsync(id);

        for (var i = 0; i < AuthService.MaxFailedAttempts; i++)
            await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, "000000"));

        var r = await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, CodeNow(secret)));
        Assert.Equal(AuthError.LockedOut, r.Error); // even the right code is refused while locked
    }

    [Fact]
    public async Task Asking_for_the_code_does_not_reset_the_failure_count()
    {
        var id = await SignUpAsync("asha@example.com");
        await EnableMfaAsync(id);

        for (var i = 0; i < 3; i++) await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, "000000"));
        await _auth.LoginAsync(new LoginRequest("asha@example.com", Password)); // password right, no code yet

        Assert.Equal(3, (await _db.Users.SingleAsync()).FailedLoginCount);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_only_once()
    {
        var id = await SignUpAsync("asha@example.com");
        var (_, codes) = await EnableMfaAsync(id);

        Assert.True((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, codes[0]))).Succeeded);
        Assert.False((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, codes[0]))).Succeeded);
        Assert.True((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password, codes[1].ToUpperInvariant().Replace("-", " ")))).Succeeded);
        Assert.Equal(2, await _db.RecoveryCodes.CountAsync(r => r.UsedAt != null));
    }

    [Fact]
    public async Task Turning_two_factor_off_needs_the_password_and_a_code_and_removes_everything()
    {
        var id = await SignUpAsync("asha@example.com");
        var (secret, _) = await EnableMfaAsync(id);

        Assert.False(await _account.DisableMfaAsync(id, Password, null, null));
        Assert.False(await _account.DisableMfaAsync(id, "wrong-password-1", CodeNow(secret), null));
        Assert.False(await _account.DisableMfaAsync(id, Password, "000000", null));
        Assert.True((await _db.Users.SingleAsync()).MfaEnabled);

        Assert.True(await _account.DisableMfaAsync(id, Password, CodeNow(secret), null));

        var user = await _db.Users.SingleAsync();
        Assert.False(user.MfaEnabled);
        Assert.Null(user.ProtectedMfaSecret);
        Assert.Empty(await _db.RecoveryCodes.ToListAsync());
        Assert.True((await _auth.LoginAsync(new LoginRequest("asha@example.com", Password))).Succeeded);
        Assert.Contains(await _db.AuditEvents.ToListAsync(), e => e.Type == AuditEventTypes.MfaDisabled);
    }

    [Fact]
    public async Task Two_factor_is_per_account()
    {
        var asha = await SignUpAsync("asha@example.com");
        await SignUpAsync("ravi@example.com");
        await EnableMfaAsync(asha);

        Assert.True((await _auth.LoginAsync(new LoginRequest("ravi@example.com", Password))).Succeeded);
    }
}
