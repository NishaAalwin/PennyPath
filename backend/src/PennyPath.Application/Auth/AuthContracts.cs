namespace PennyPath.Application.Auth;

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime ExpiresAt, string Email);

public enum AuthError { None, InvalidInput, EmailTaken, InvalidCredentials, LockedOut }

public record AuthResult(AuthResponse? Response, AuthError Error, string? Message = null)
{
    public bool Succeeded => Error == AuthError.None;
}

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct = default);
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) Create(Guid userId, string email);
}
