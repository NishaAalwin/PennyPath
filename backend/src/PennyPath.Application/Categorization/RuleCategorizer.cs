using System.Text.RegularExpressions;
using PennyPath.Domain.Entities;

namespace PennyPath.Application.Categorization;

public static class Categories
{
    public const string Uncategorized = "Uncategorized";
    public const string Income = "Income";

    public static readonly IReadOnlyList<string> All =
    [
        "Food & Dining", "Groceries", "Transport", "Shopping", "Bills & Utilities",
        "Entertainment", "Health", "Travel", "Education", "Rent", "Transfers", Income, Uncategorized,
    ];

    public static bool IsValid(string? category) => category is not null && All.Contains(category);
}

public interface ICategorizer
{
    /// <summary>Picks a category from the merchant text only. Never returns null.</summary>
    string Categorize(string? merchant, TransactionType type);
}

/// <summary>Keyword rules over the merchant text. Runs locally; nothing leaves the server.</summary>
public sealed class RuleCategorizer : ICategorizer
{
    private static readonly (string Category, string[] Keywords)[] Rules =
    [
        ("Food & Dining", ["swiggy", "zomato", "restaurant", "cafe", "coffee", "starbucks", "dominos", "pizza", "mcdonalds", "kfc", "bakery", "biryani", "eatsure"]),
        ("Groceries", ["bigbasket", "blinkit", "zepto", "dmart", "grocery", "supermarket", "instamart", "reliance fresh", "more retail"]),
        ("Transport", ["uber", "ola", "rapido", "metro", "fuel", "petrol", "diesel", "fastag", "irctc", "indian oil", "hpcl", "bpcl", "parking"]),
        ("Shopping", ["amazon", "flipkart", "myntra", "ajio", "meesho", "nykaa", "croma", "decathlon"]),
        ("Bills & Utilities", ["electricity", "airtel", "jio", "vodafone", "vi prepaid", "bsnl", "broadband", "recharge", "water bill", "gas bill", "bescom", "insurance", "lic"]),
        ("Entertainment", ["netflix", "spotify", "hotstar", "prime video", "bookmyshow", "pvr", "inox", "youtube", "sonyliv", "gaana"]),
        ("Health", ["pharmacy", "apollo", "medplus", "1mg", "pharmeasy", "hospital", "clinic", "diagnostic", "lab"]),
        ("Travel", ["makemytrip", "goibibo", "cleartrip", "airline", "indigo", "air india", "hotel", "oyo", "booking.com"]),
        ("Education", ["udemy", "coursera", "school", "college", "tuition", "byju"]),
        ("Rent", ["rent", "landlord"]),
    ];

    private static readonly (string Category, Regex Pattern)[] Compiled = Rules
        .Select(r => (r.Category, new Regex(
            @"\b(?:" + string.Join("|", r.Keywords.Select(Regex.Escape)) + @")\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(50))))
        .ToArray();

    public string Categorize(string? merchant, TransactionType type)
    {
        if (!string.IsNullOrWhiteSpace(merchant))
        {
            foreach (var (category, pattern) in Compiled)
                if (pattern.IsMatch(merchant)) return category;
        }

        return type == TransactionType.Income ? Categories.Income : Categories.Uncategorized;
    }
}
