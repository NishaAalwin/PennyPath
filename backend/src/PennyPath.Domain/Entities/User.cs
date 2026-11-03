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
}
