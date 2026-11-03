namespace PennyPath.Domain.Entities;

/// <summary>A security-relevant event (sign-in, failure, logout, consent change). Holds no financial data.</summary>
public class AuditEvent
{
    public const int MaxIpLength = 45;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Null when the event is not tied to an account (a failed sign-in for an unknown email).</summary>
    public Guid? UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string? Ip { get; set; }
}
