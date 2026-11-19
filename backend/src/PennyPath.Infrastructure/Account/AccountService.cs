using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PennyPath.Application.Account;
using PennyPath.Application.Auth;
using PennyPath.Application.Gmail;
using PennyPath.Application.Security;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Auth;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Account;

public class AppOptions
{
    public const string Section = "App";

    /// <summary>The address people open the app at; used to build the links in emails.</summary>
    public string PublicUrl { get; set; } = "https://localhost:8443";
}

public class AccountService(
    AppDbContext db, IEmailSender email, IAuthService auth, IGmailService gmail, ITokenProtector protector,
    ITransactionService transactions, IAuditLog audit, TimeProvider clock, IOptions<AppOptions> app) : IAccountService
{
    public static readonly TimeSpan VerifyLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(30);
    public const int MaxLinksPerHour = 3;
    public const int RecoveryCodeCount = 10;
    private const string Issuer = "PennyPath";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<AccountInfo?> InfoAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return null;
        return new AccountInfo(u.Email, u.EmailVerified, u.MfaEnabled, await db.GmailConnections.AnyAsync(g => g.UserId == userId, ct));
    }

    // ---- email verification ----

    public async Task SendVerificationAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.EmailVerified) return;

        var raw = await IssueTokenAsync(userId, UserTokenPurpose.VerifyEmail, VerifyLifetime, ct);
        if (raw is null) return; // asked too often; the links already sent still work
        await email.SendAsync(new EmailMessage(user.Email, "Confirm your email for PennyPath",
            $"Welcome to PennyPath.\n\nConfirm your email address by opening this link:\n{Link("verify", raw)}\n\n"
            + "The link works once and expires in 24 hours. If you did not create an account, ignore this email."), ct);
    }

    public async Task<bool> VerifyEmailAsync(string? token, string? ip, CancellationToken ct = default)
    {
        var t = await FindUsableAsync(token, UserTokenPurpose.VerifyEmail, ct);
        if (t is null) return false;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == t.UserId, ct);
        if (user is null) return false;
        t.UsedAt = Now;
        user.EmailVerified = true;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(user.Id, AuditEventTypes.EmailVerified, ip, ct);
        return true;
    }

    // ---- password reset ----

    public async Task RequestPasswordResetAsync(string? emailAddress, string? ip, CancellationToken ct = default)
    {
        var normalized = (emailAddress ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > 254) return;

        // The same work is done and the same answer given whether or not the address has an account.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
        if (user is null) return;

        var raw = await IssueTokenAsync(user.Id, UserTokenPurpose.ResetPassword, ResetLifetime, ct);
        if (raw is null) return;
        await email.SendAsync(new EmailMessage(user.Email, "Reset your PennyPath password",
            $"Someone asked to reset the password for this PennyPath account.\n\nChoose a new password here:\n{Link("reset", raw)}\n\n"
            + "The link works once and expires in 30 minutes. If this was not you, ignore this email: your password has not changed."), ct);
        await audit.RecordAsync(user.Id, AuditEventTypes.PasswordResetRequested, ip, ct);
    }

    public async Task<ResetResult> ResetPasswordAsync(string? token, string? newPassword, string? ip, CancellationToken ct = default)
    {
        var t = await FindUsableAsync(token, UserTokenPurpose.ResetPassword, ct);
        if (t is null) return new ResetResult(ResetOutcome.InvalidToken, "This link is invalid or has expired. Ask for a new one.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == t.UserId, ct);
        if (user is null) return new ResetResult(ResetOutcome.InvalidToken, "This link is invalid or has expired. Ask for a new one.");

        var weak = AuthService.CheckPassword(newPassword, user.Email);
        if (weak is not null) return new ResetResult(ResetOutcome.WeakPassword, weak); // the link stays valid so the user can try again

        var now = Now;
        user.PasswordHash = AuthService.HashPassword(user, newPassword!);
        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        user.EmailVerified = true; // opening a link sent to this address proves the user controls it
        t.UsedAt = now;
        foreach (var other in await db.UserTokens.Where(x => x.UserId == user.Id && x.Purpose == UserTokenPurpose.ResetPassword && x.UsedAt == null).ToListAsync(ct))
            other.UsedAt = now;
        await db.SaveChangesAsync(ct);

        await auth.LogoutAllAsync(user.Id, ct); // anyone who had the old password is signed out everywhere
        await audit.RecordAsync(user.Id, AuditEventTypes.PasswordReset, ip, ct);
        return new ResetResult(ResetOutcome.Done);
    }

    // ---- deleting the account ----

    public async Task<DeleteOutcome> DeleteAccountAsync(Guid userId, string? password, string? code, string? ip, CancellationToken ct = default)
    {
        switch (await auth.ReauthenticateAsync(userId, password, code, ct))
        {
            case ReauthResult.LockedOut: return DeleteOutcome.LockedOut;
            case ReauthResult.MfaRequired: return DeleteOutcome.MfaRequired;
            case ReauthResult.Invalid: return DeleteOutcome.NotAuthenticated;
        }

        await gmail.DisconnectAsync(userId, ip, ct); // revokes the Google grant and deletes the stored token

        if (db.Database.IsRelational())
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Transactions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Sessions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.AuditEvents.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.GmailConnections.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.GmailOAuthStates.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.RecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Users.Where(x => x.Id == userId).ExecuteDeleteAsync(ct);
            await tx.CommitAsync(ct);
        }
        else
        {
            db.Transactions.RemoveRange(db.Transactions.Where(x => x.UserId == userId));
            db.Sessions.RemoveRange(db.Sessions.Where(x => x.UserId == userId));
            db.AuditEvents.RemoveRange(db.AuditEvents.Where(x => x.UserId == userId));
            db.GmailConnections.RemoveRange(db.GmailConnections.Where(x => x.UserId == userId));
            db.GmailOAuthStates.RemoveRange(db.GmailOAuthStates.Where(x => x.UserId == userId));
            db.UserTokens.RemoveRange(db.UserTokens.Where(x => x.UserId == userId));
            db.RecoveryCodes.RemoveRange(db.RecoveryCodes.Where(x => x.UserId == userId));
            db.Users.RemoveRange(db.Users.Where(x => x.Id == userId));
            await db.SaveChangesAsync(ct);
        }

        // Kept without any link to the person: the fact that an account was deleted, and from where.
        await audit.RecordAsync(null, AuditEventTypes.AccountDeleted, ip, ct);
        return DeleteOutcome.Deleted;
    }

    // ---- export ----

    public async Task<AccountExport?> ExportAsync(Guid userId, string? ip, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return null;

        var events = await db.AuditEvents.AsNoTracking().Where(e => e.UserId == userId)
            .OrderByDescending(e => e.At).Take(1000).Select(e => new ExportedEvent(e.Type, e.At)).ToListAsync(ct);
        var export = new AccountExport(
            Now, u.Email, u.CreatedAt, u.EmailVerified, u.MfaEnabled, u.AiConsentAt is not null,
            await db.GmailConnections.AnyAsync(g => g.UserId == userId, ct),
            await transactions.ListAsync(userId, null, ct), events);

        await audit.RecordAsync(userId, AuditEventTypes.DataExported, ip, ct);
        return export;
    }

    // ---- two-factor login ----

    public async Task<MfaSetup?> BeginMfaSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.MfaEnabled) return null;

        var secret = Totp.NewSecret();
        var base32 = Totp.ToBase32(secret);
        user.ProtectedMfaSecret = protector.Protect(base32); // kept encrypted; not active until a code proves the app has it
        user.MfaLastStep = 0;
        await db.SaveChangesAsync(ct);
        return new MfaSetup(base32, Totp.OtpAuthUri(Issuer, user.Email, secret));
    }

    public async Task<MfaEnableResult> EnableMfaAsync(Guid userId, string? code, string? ip, CancellationToken ct = default)
    {
        var none = new MfaEnableResult(false, []);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || user.MfaEnabled || user.ProtectedMfaSecret is null) return none;

        var secret = protector.Unprotect(user.ProtectedMfaSecret) is { } b32 ? Totp.FromBase32(b32) : null;
        var step = secret is null ? null : Totp.Verify(secret, MfaVerifier.Normalize(code), clock.GetUtcNow(), 0);
        if (step is null) return none;

        var codes = MfaVerifier.NewRecoveryCodes(RecoveryCodeCount);
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(r => r.UserId == userId).ToListAsync(ct));
        db.RecoveryCodes.AddRange(codes.Select(c => new RecoveryCode { UserId = userId, CodeHash = MfaVerifier.HashRecoveryCode(MfaVerifier.Normalize(c)) }));
        user.MfaEnabled = true;
        user.MfaLastStep = step.Value;
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(userId, AuditEventTypes.MfaEnabled, ip, ct);
        return new MfaEnableResult(true, codes);
    }

    public async Task<bool> DisableMfaAsync(Guid userId, string? password, string? code, string? ip, CancellationToken ct = default)
    {
        if (await auth.ReauthenticateAsync(userId, password, code, ct) != ReauthResult.Ok) return false;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return false;
        var wasOn = user.MfaEnabled;
        user.MfaEnabled = false;
        user.ProtectedMfaSecret = null;
        user.MfaLastStep = 0;
        db.RecoveryCodes.RemoveRange(await db.RecoveryCodes.Where(r => r.UserId == userId).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        if (wasOn) await audit.RecordAsync(userId, AuditEventTypes.MfaDisabled, ip, ct);
        return true;
    }

    // ---- link tokens ----

    /// <summary>Creates a link token, voids earlier unused ones of the same kind, and returns the raw token (stored only as a hash).</summary>
    private async Task<string?> IssueTokenAsync(Guid userId, string purpose, TimeSpan life, CancellationToken ct)
    {
        var now = Now;
        var recent = await db.UserTokens.CountAsync(t => t.UserId == userId && t.Purpose == purpose && t.CreatedAt > now.AddHours(-1), ct);
        if (recent >= MaxLinksPerHour) return null;

        foreach (var old in await db.UserTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAt == null).ToListAsync(ct))
            old.UsedAt = now;
        db.UserTokens.RemoveRange(await db.UserTokens.Where(t => t.CreatedAt < now.AddDays(-7)).Take(100).ToListAsync(ct));

        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.UserTokens.Add(new UserToken { UserId = userId, Purpose = purpose, TokenHash = Sha256Hex(raw), CreatedAt = now, ExpiresAt = now + life });
        await db.SaveChangesAsync(ct);
        return raw;
    }

    private async Task<UserToken?> FindUsableAsync(string? raw, string purpose, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 100) return null;
        var hash = Sha256Hex(raw);
        var t = await db.UserTokens.FirstOrDefaultAsync(x => x.TokenHash == hash && x.Purpose == purpose, ct);
        return t is { UsedAt: null } && t.ExpiresAt > Now ? t : null;
    }

    // The token goes after # so browsers never send it to a server, a log or another site.
    private string Link(string kind, string token) => $"{app.Value.PublicUrl.TrimEnd('/')}/#{kind}={token}";

    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
