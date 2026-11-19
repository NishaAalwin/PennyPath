namespace PennyPath.Application.Auth;

public record RegisterRequest(string Email, string Password);
/// <param name="TotpCode">The 6-digit code (or a recovery code) when the account has two-factor login on.</param>
public record LoginRequest(string Email, string Password, string? TotpCode = null);
public record AuthResponse(string Token, DateTime ExpiresAt, string Email);

public enum AuthError { None, InvalidInput, EmailTaken, InvalidCredentials, LockedOut, MfaRequired }

public enum ReauthResult { Ok, Invalid, LockedOut, MfaRequired }

public record AuthResult(AuthResponse? Response, AuthError Error, string? Message = null, Guid? UserId = null)
{
    public bool Succeeded => Error == AuthError.None;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>
    /// Checks the password (and the two-factor code when it is on) again before something dangerous such as deleting the
    /// account. Wrong answers count toward the same lockout as sign-in.
    /// </summary>
    Task<ReauthResult> ReauthenticateAsync(Guid userId, string? password, string? code, CancellationToken ct = default);

    /// <summary>Ends one session so its token stops working at once. False when it was not found.</summary>
    Task<bool> LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Ends every active session of the user and returns how many were ended.</summary>
    Task<int> LogoutAllAsync(Guid userId, CancellationToken ct = default);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) Create(Guid userId, string email, Guid sessionId);
}
