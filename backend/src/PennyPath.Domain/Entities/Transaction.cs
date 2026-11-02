namespace PennyPath.Domain.Entities;

public enum TransactionType { Income, Expense }

public class Transaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public decimal Amount { get; set; }
    public TransactionType Type { get; set; }
    public string Merchant { get; set; } = string.Empty;
    public string Category { get; set; } = "Uncategorized";
}
