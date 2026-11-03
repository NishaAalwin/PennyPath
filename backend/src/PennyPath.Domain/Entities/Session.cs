namespace PennyPath.Domain.Entities;

/// <summary>One signed-in browser. The login token carries this id, so ending the session revokes the token at once.</summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
