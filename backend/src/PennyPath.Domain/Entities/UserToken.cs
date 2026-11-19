namespace PennyPath.Domain.Entities;

public static class UserTokenPurpose
{
    public const string VerifyEmail = "VerifyEmail";
    public const string ResetPassword = "ResetPassword";
}

/// <summary>A single-use link token (verify email, reset password). Only its SHA-256 is stored, never the token itself.</summary>
public class UserToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    /// <summary>Set when the token was used or replaced by a newer one; a used token never works again.</summary>
    public DateTime? UsedAt { get; set; }
}

/// <summary>A one-time backup code for two-factor login; only its SHA-256 is stored.</summary>
public class RecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime? UsedAt { get; set; }
}
