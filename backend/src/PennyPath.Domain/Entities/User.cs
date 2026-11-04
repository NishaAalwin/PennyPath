namespace PennyPath.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEnd { get; set; }

    /// <summary>When the user agreed to send merchant text or aggregates to an AI service; null means no consent (the default).</summary>
    public DateTime? AiConsentAt { get; set; }

    /// <summary>True once the user opened the link emailed to this address. Data endpoints refuse unverified accounts.</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Two-factor login (TOTP) is on. The secret is stored encrypted; while this is false a non-null secret is an unconfirmed setup.</summary>
    public bool MfaEnabled { get; set; }
    public string? ProtectedMfaSecret { get; set; }

    /// <summary>The last 30-second step accepted, so one code cannot be used twice.</summary>
    public long MfaLastStep { get; set; }
}
