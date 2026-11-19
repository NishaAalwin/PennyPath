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
    public TransactionSource Source { get; set; }

    /// <summary>A refund or reversal. It offsets the expense it links to and must not count as income.</summary>
    public bool IsRefund { get; set; }
    public Guid? RefundOfId { get; set; }

    /// <summary>Set when this row looks like a second message about the same payment; excluded from totals until the user decides.</summary>
    public Guid? DuplicateOfId { get; set; }

    /// <summary>Money moved between the user's own accounts; not income or expense.</summary>
    public bool IsTransfer { get; set; }

    /// <summary>SHA-256 of the normalized source text; with UserId it is unique, so re-importing a message is a no-op.</summary>
    public string RawHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
