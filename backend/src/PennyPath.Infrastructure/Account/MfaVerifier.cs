using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Account;
using PennyPath.Application.Security;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Account;

public class MfaVerifier(AppDbContext db, ITokenProtector protector, TimeProvider clock) : IMfaVerifier
{
    public const int RecoveryCodeLength = 10;
    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789"; // no 0/o/1/i/l to avoid misreading

    public async Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null || !user.MfaEnabled || user.ProtectedMfaSecret is null) return false;

        var normalized = Normalize(code);
        if (normalized.Length == Totp.Digits && normalized.All(char.IsAsciiDigit))
        {
            var secret = Secret(user.ProtectedMfaSecret);
            if (secret is null) return false;
            var step = Totp.Verify(secret, normalized, clock.GetUtcNow(), user.MfaLastStep);
            if (step is null) return false;
            user.MfaLastStep = step.Value;
            await db.SaveChangesAsync(ct);
            return true;
        }

        if (normalized.Length == RecoveryCodeLength)
        {
            var hash = HashRecoveryCode(normalized);
            var recovery = await db.RecoveryCodes.FirstOrDefaultAsync(r => r.UserId == userId && r.CodeHash == hash && r.UsedAt == null, ct);
            if (recovery is null) return false;
            recovery.UsedAt = clock.GetUtcNow().UtcDateTime; // each backup code works once
            await db.SaveChangesAsync(ct);
            return true;
        }

        return false;
    }

    public byte[]? Secret(string protectedSecret) =>
        protector.Unprotect(protectedSecret) is { } base32 ? Totp.FromBase32(base32) : null;

    public static string Normalize(string? code) => (code ?? string.Empty).Replace(" ", "").Replace("-", "").Trim().ToLowerInvariant();

    public static string HashRecoveryCode(string normalized) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToLowerInvariant())));

    /// <summary>Ten random codes shown to the user once, formatted abcde-fghij.</summary>
    public static List<string> NewRecoveryCodes(int count = 10) =>
        Enumerable.Range(0, count).Select(_ =>
        {
            var chars = RandomNumberGenerator.GetItems<char>(RecoveryAlphabet.AsSpan(), RecoveryCodeLength);
            var raw = new string(chars);
            return raw[..5] + "-" + raw[5..];
        }).ToList();
}
