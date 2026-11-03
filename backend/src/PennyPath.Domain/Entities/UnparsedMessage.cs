namespace PennyPath.Domain.Entities;

/// <summary>A pasted message the parser could not read, kept (capped) until the user dismisses it.</summary>
public class UnparsedMessage
{
    public const int MaxTextLength = 500;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string RawHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
