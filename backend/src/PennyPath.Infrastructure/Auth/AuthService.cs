using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Auth;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Auth;

public class AuthService(AppDbContext db, ITokenService tokens, TimeProvider clock) : IAuthService
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128; // bounds hashing cost so huge inputs cannot be used for DoS
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<User> Hasher = new();

    // Verified against when the email is unknown so response time does not reveal which emails exist.
    private static readonly string DummyHash = Hasher.HashPassword(new User(), Guid.NewGuid().ToString("N"));

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "12345678", "123456789", "1234567890", "qwerty123",
        "qwertyuiop", "iloveyou", "11111111", "00000000", "abc12345", "admin123", "welcome1", "letmein123",
    };

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        if (!IsValidEmail(email))
            return new AuthResult(null, AuthError.InvalidInput, "A valid email is required.");

        var weak = CheckPassword(request.Password, email);
        if (weak is not null) return new AuthResult(null, AuthError.InvalidInput, weak);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return new AuthResult(null, AuthError.EmailTaken, "An account with this email already exists.");

        var user = new User { Email = email };
        user.PasswordHash = Hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            if (!await db.Users.AnyAsync(u => u.Email == email, ct)) throw;
            // Lost a race with a concurrent registration; the unique index caught it.
            return new AuthResult(null, AuthError.EmailTaken, "An account with this email already exists.");
        }
        return await SuccessAsync(user, ct);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        var password = request.Password ?? string.Empty;
        if (password.Length > MaxPasswordLength) password = password[..MaxPasswordLength];

        var invalid = new AuthResult(null, AuthError.InvalidCredentials, "Invalid email or password.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            Hasher.VerifyHashedPassword(new User(), DummyHash, password);
            return invalid;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (user.LockoutEnd > now)
            return new AuthResult(null, AuthError.LockedOut, "Too many failed attempts. Try again later.", user.Id);

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedAttempts)
            {
                user.LockoutEnd = now + LockoutDuration;
                user.FailedLoginCount = 0;
            }
            await db.SaveChangesAsync(ct);
            return invalid with { UserId = user.Id };
        }

        user.FailedLoginCount = 0;
        user.LockoutEnd = null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = Hasher.HashPassword(user, password);
        await db.SaveChangesAsync(ct);
        return await SuccessAsync(user, ct);
    }

    public async Task<bool> LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null) return false;
        session.RevokedAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> LogoutAllAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var active = await db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now).ToListAsync(ct);
        foreach (var s in active) s.RevokedAt = now;
        await db.SaveChangesAsync(ct);
        return active.Count;
    }

    private static string? CheckPassword(string? password, string email)
    {
        if (password is null || password.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";
        if (password.Length > MaxPasswordLength)
            return $"Password must be at most {MaxPasswordLength} characters.";
        if (CommonPasswords.Contains(password))
            return "That password is too common. Choose a less guessable one.";
        var local = email.Split('@')[0];
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            return "Password must not contain your email name.";
        return null;
    }

    private async Task<AuthResult> SuccessAsync(User user, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var sessionId = Guid.NewGuid();
        var (token, expires) = tokens.Create(user.Id, user.Email, sessionId);

        // Drop this user's long-dead sessions so the table does not grow without bound.
        db.Sessions.RemoveRange(await db.Sessions.Where(s => s.UserId == user.Id && s.ExpiresAt < now.AddDays(-1)).Take(100).ToListAsync(ct));
        db.Sessions.Add(new Session { Id = sessionId, UserId = user.Id, CreatedAt = now, ExpiresAt = expires });
        await db.SaveChangesAsync(ct);

        return new AuthResult(new AuthResponse(token, expires, user.Email), AuthError.None, UserId: user.Id);
    }

    private static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= 254 && MailAddress.TryCreate(email, out var a) && a.Address == email && email.Contains('.');
}
