using PennyPath.Domain.Entities;

namespace PennyPath.Application.Parsing;

public record ParsedTransaction(
    decimal Amount,
    TransactionType Type,
    DateOnly Date,
    string? Merchant,
    string? Account);

public interface ITransactionParser
{
    /// <summary>Returns null when the text is not a completed money movement (OTPs, offers, reminders, unknown formats).</summary>
    ParsedTransaction? Parse(string text, DateOnly fallbackDate);
}
