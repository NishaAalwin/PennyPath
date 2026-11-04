using System.Net;
using System.Text.RegularExpressions;

namespace PennyPath.Application.Parsing;

/// <summary>
/// Turns an alert email into the short text the SMS parser understands. Emails carry long footers ("never share your
/// OTP", disclaimers) that would make the parser discard a genuine alert, so the text is cut where the footer starts
/// and kept short. HTML is reduced to plain text.
/// </summary>
public static partial class EmailTextExtractor
{
    public const int MaxLength = 600;

    private static readonly string[] FooterMarkers =
    [
        "do not share", "don't share", "never share", "disclaimer", "if you did not", "if you have not", "if this transaction",
        "if you don't recognise", "if you do not recognise", "warm regards", "regards,", "sincerely", "this is a system generated",
        "this is an auto", "please do not reply", "do not reply", "unsubscribe", "terms and conditions", "t&c",
    ];

    public static string ToPlainText(string html)
    {
        var text = StyleOrScript().Replace(html, " ");
        text = BlockBreak().Replace(text, " ");
        text = Tag().Replace(text, " ");
        return WebUtility.HtmlDecode(text);
    }

    public static string BuildParsableText(string subject, string body)
    {
        var text = Whitespace().Replace($"{subject} {body}".Trim(), " ");
        var lower = text.ToLowerInvariant();
        var cut = FooterMarkers.Select(m => lower.IndexOf(m, StringComparison.Ordinal)).Where(i => i > 0).DefaultIfEmpty(-1).Min();
        if (cut > 0) text = text[..cut];
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    [GeneratedRegex(@"<(style|script)\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleOrScript();

    [GeneratedRegex(@"<(br|/p|/div|/tr|/td|/li|/h\d)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
