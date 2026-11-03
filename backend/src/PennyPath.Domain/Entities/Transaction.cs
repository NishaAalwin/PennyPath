namespace PennyPath.Domain.Entities;

public enum TransactionType { Income, Expense }

public enum TransactionSource { Manual, Sms, Email }

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string Merchant { get; set; } = string.Empty;
    public string Category { get; set; } = "Uncategorized";

    /// <summary>True once the user chose the category; automatic categorization never changes such a row.</summary>
    public bool CategoryIsManual { get; set; }
    public string? Account { get; set; }
    public TransactionSource Source { get; set; }

    /// <summary>SHA-256 of the normalized source text; with UserId it is unique, so re-importing a message is a no-op.</summary>
    public string RawHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
