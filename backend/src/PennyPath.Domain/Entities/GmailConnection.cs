namespace PennyPath.Domain.Entities;

/// <summary>A user's link to their Gmail account. Only the encrypted refresh token is kept, never email content.</summary>
public class GmailConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>Encrypted with <c>ITokenProtector</c>; never a plain column and never sent to the browser.</summary>
    public string ProtectedRefreshToken { get; set; } = string.Empty;

    public DateTime ConnectedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
}

/// <summary>A pending consent request: single-use, short-lived, tied to the user who started it.</summary>
public class GmailOAuthState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }

    /// <summary>SHA-256 of the random <c>state</c> sent to Google, so a database leak cannot be replayed.</summary>
    public string StateHash { get; set; } = string.Empty;

    /// <summary>PKCE verifier, encrypted.</summary>
    public string ProtectedCodeVerifier { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}
