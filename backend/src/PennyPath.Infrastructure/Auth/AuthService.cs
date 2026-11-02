using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PennyPath.Application.Auth;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Infrastructure.Auth;

public class AuthService(AppDbContext db, ITokenService tokens) : IAuthService
{
    public const int MinPasswordLength = 8;
    private static readonly PasswordHasher<User> Hasher = new();

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        if (!IsValidEmail(email))
            return new AuthResult(null, AuthError.InvalidInput, "A valid email is required.");
        if (request.Password is null || request.Password.Length < MinPasswordLength)
            return new AuthResult(null, AuthError.InvalidInput, $"Password must be at least {MinPasswordLength} characters.");

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
        return Success(user);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var email = Normalize(request.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        var invalid = new AuthResult(null, AuthError.InvalidCredentials, "Invalid email or password.");
        if (user is null || request.Password is null) return invalid;

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return invalid;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }
        return Success(user);
    }

    private AuthResult Success(User user)
    {
        var (token, expires) = tokens.Create(user.Id, user.Email);
        return new AuthResult(new AuthResponse(token, expires, user.Email), AuthError.None);
    }

    private static string Normalize(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email) =>
        email.Length is > 0 and <= 254 && MailAddress.TryCreate(email, out var a) && a.Address == email && email.Contains('.');
}
