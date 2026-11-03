namespace PennyPath.Application.Auth;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime ExpiresAt, string Email);

public enum AuthError { None, InvalidInput, EmailTaken, InvalidCredentials, LockedOut }

public record AuthResult(AuthResponse? Response, AuthError Error, string? Message = null, Guid? UserId = null)
{
    public bool Succeeded => Error == AuthError.None;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);

    /// <summary>Ends one session so its token stops working at once. False when it was not found.</summary>
    Task<bool> LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct = default);

    /// <summary>Ends every active session of the user and returns how many were ended.</summary>
    Task<int> LogoutAllAsync(Guid userId, CancellationToken ct = default);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) Create(Guid userId, string email, Guid sessionId);
}
