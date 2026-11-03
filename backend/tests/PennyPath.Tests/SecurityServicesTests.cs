using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using PennyPath.Application.Security;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Security;

namespace PennyPath.Tests;

public sealed class TokenProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pp-keys-" + Guid.NewGuid().ToString("N"));

    private DataProtectionTokenProtector Protector(string? dir = null) =>
        new(DataProtectionProvider.Create(new DirectoryInfo(dir ?? _dir), o => o.SetApplicationName("PennyPath")));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void A_secret_survives_a_round_trip_and_is_not_stored_in_the_clear()
    {
        var p = Protector();
        var secret = "1//0gRefreshTokenValue-abc123";

        var stored = p.Protect(secret);

        Assert.DoesNotContain("RefreshTokenValue", stored);
        Assert.Equal(secret, p.Unprotect(stored));
    }

    [Fact]
    public void The_same_secret_encrypts_differently_each_time()
    {
        var p = Protector();
        Assert.NotEqual(p.Protect("same"), p.Protect("same"));
    }

    [Fact]
    public void A_tampered_or_garbage_value_is_refused_not_decrypted()
    {
        var p = Protector();
        var stored = p.Protect("secret");
        var tampered = stored[..^4] + (stored[^4] == 'A' ? "BBBB" : "AAAA");

        Assert.Null(p.Unprotect(tampered));
        Assert.Null(p.Unprotect("not-a-protected-value"));
        Assert.Null(p.Unprotect(string.Empty));
    }

    [Fact]
    public void A_value_protected_for_another_purpose_cannot_be_read()
    {
        var provider = DataProtectionProvider.Create(new DirectoryInfo(_dir), o => o.SetApplicationName("PennyPath"));
        var foreign = provider.CreateProtector("some.other.purpose").Protect("secret");

        Assert.Null(new DataProtectionTokenProtector(provider).Unprotect(foreign));
    }

    [Fact]
    public void Secrets_stay_readable_after_a_restart_because_the_key_ring_is_on_disk()
    {
        var stored = Protector().Protect("refresh-token");

        var afterRestart = Protector(); // a brand-new provider reading the same key directory

        Assert.Equal("refresh-token", afterRestart.Unprotect(stored));
        Assert.NotEmpty(Directory.GetFiles(_dir, "key-*.xml"));
    }

    [Fact]
    public void Without_the_key_ring_the_secret_is_unreadable()
    {
        var stored = Protector().Protect("refresh-token");
        var elsewhere = Path.Combine(Path.GetTempPath(), "pp-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(Protector(elsewhere).Unprotect(stored));
        }
        finally
        {
            if (Directory.Exists(elsewhere)) Directory.Delete(elsewhere, recursive: true);
        }
    }
}

public class AuditLogAndConsentTests
{
    private static readonly Guid Asha = Guid.NewGuid();
    private static readonly Guid Ravi = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Events_come_back_newest_first_and_only_for_that_account()
    {
        using var db = NewDb();
        var clock = new FakeTimeProvider(Now);
        var log = new AuditLog(db, clock);
        await log.RecordAsync(Asha, "LoginSucceeded", "10.0.0.1");
        clock.Advance(TimeSpan.FromMinutes(5));
        await log.RecordAsync(Asha, "LoggedOut", "10.0.0.1");
        await log.RecordAsync(Ravi, "LoginFailed", "10.0.0.2");

        var asha = await log.RecentAsync(Asha);

        Assert.Equal(["LoggedOut", "LoginSucceeded"], asha.Select(e => e.Type));
        Assert.DoesNotContain(asha, e => e.Type == "LoginFailed");
    }

    [Fact]
    public async Task Events_older_than_the_retention_period_are_removed_as_new_ones_arrive()
    {
        using var db = NewDb();
        var clock = new FakeTimeProvider(Now);
        var log = new AuditLog(db, clock);
        await log.RecordAsync(Asha, "Old", null);
        await log.RecordAsync(null, "OldAnonymous", null);

        clock.Advance(AuditLog.Retention + TimeSpan.FromDays(1));
        await log.RecordAsync(Asha, "New", null);

        Assert.Equal(["New"], (await db.AuditEvents.ToListAsync()).Select(e => e.Type));
    }

    [Fact]
    public async Task An_overlong_ip_is_cut_to_fit_and_nothing_else_is_stored()
    {
        using var db = NewDb();
        var log = new AuditLog(db, new FakeTimeProvider(Now));

        await log.RecordAsync(Asha, "LoginFailed", new string('1', 200));

        var row = await db.AuditEvents.SingleAsync();
        Assert.Equal(AuditEvent.MaxIpLength, row.Ip!.Length);
        Assert.Equal(["Id", "UserId", "Type", "At", "Ip"], typeof(AuditEvent).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task Ai_consent_is_off_until_granted_and_withdrawing_turns_it_off_again()
    {
        using var db = NewDb();
        db.Users.Add(new User { Id = Asha, Email = "a@b.com" });
        await db.SaveChangesAsync();
        var svc = new AiConsentService(db, new FakeTimeProvider(Now));

        Assert.False(await svc.IsEnabledAsync(Asha));
        Assert.True(await svc.SetAsync(Asha, true));
        Assert.True(await svc.IsEnabledAsync(Asha));
        Assert.True(await svc.SetAsync(Asha, false));
        Assert.False(await svc.IsEnabledAsync(Asha));
    }

    [Fact]
    public async Task Granting_twice_keeps_the_first_consent_time()
    {
        using var db = NewDb();
        db.Users.Add(new User { Id = Asha, Email = "a@b.com" });
        await db.SaveChangesAsync();
        var clock = new FakeTimeProvider(Now);
        var svc = new AiConsentService(db, clock);

        await svc.SetAsync(Asha, true);
        var first = (await db.Users.SingleAsync()).AiConsentAt;
        clock.Advance(TimeSpan.FromDays(3));
        await svc.SetAsync(Asha, true);

        Assert.Equal(first, (await db.Users.SingleAsync()).AiConsentAt);
    }

    [Fact]
    public async Task Consent_for_an_unknown_user_is_refused_and_never_enabled()
    {
        using var db = NewDb();
        var svc = new AiConsentService(db, new FakeTimeProvider(Now));

        Assert.False(await svc.SetAsync(Guid.NewGuid(), true));
        Assert.False(await svc.IsEnabledAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task One_users_consent_never_applies_to_another_user()
    {
        using var db = NewDb();
        db.Users.AddRange(new User { Id = Asha, Email = "a@b.com" }, new User { Id = Ravi, Email = "r@b.com" });
        await db.SaveChangesAsync();
        var svc = new AiConsentService(db, new FakeTimeProvider(Now));

        await svc.SetAsync(Asha, true);

        Assert.False(await svc.IsEnabledAsync(Ravi));
    }
}
