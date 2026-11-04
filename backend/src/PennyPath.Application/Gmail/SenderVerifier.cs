namespace PennyPath.Application.Gmail;

/// <summary>
/// Decides whether an email really came from a known bank or payment app. A search on <c>from:</c> is not enough
/// because anyone can write any From address; what cannot be faked is Gmail's own verdict on the sender's signature
/// (DKIM) or DMARC policy, recorded in an Authentication-Results header added by Gmail's servers.
/// </summary>
public static class SenderVerifier
{
    /// <summary>Only headers written by Gmail count; a forged header from the sender names a different server.</summary>
    public const string TrustedAuthServer = "mx.google.com";

    public enum Verdict { Verified, UnknownSender, NotAuthenticated }

    public static Verdict Check(string? from, IReadOnlyList<string>? authenticationResults, IReadOnlyList<string> allowedDomains)
    {
        var fromDomain = DomainOf(from);
        if (fromDomain is null || !IsAllowed(fromDomain, allowedDomains)) return Verdict.UnknownSender;

        foreach (var header in authenticationResults ?? [])
        {
            var parts = header.Split(';', StringSplitOptions.TrimEntries);
            if (!string.Equals(parts[0].Split(' ')[0], TrustedAuthServer, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var part in parts.Skip(1))
            {
                var authenticated = part switch
                {
                    _ when part.StartsWith("dkim=pass", StringComparison.OrdinalIgnoreCase) => Property(part, "header.d") ?? Property(part, "header.i")?.TrimStart('@'),
                    _ when part.StartsWith("dmarc=pass", StringComparison.OrdinalIgnoreCase) => Property(part, "header.from"),
                    _ => null,
                };
                if (authenticated is null) continue;

                var d = authenticated.ToLowerInvariant();
                if (IsAllowed(d, allowedDomains) && Aligned(fromDomain, d)) return Verdict.Verified;
            }
        }

        return Verdict.NotAuthenticated;
    }

    /// <summary>Equal, or one is a subdomain of the other (for example alerts.hdfcbank.net signed by hdfcbank.net).</summary>
    private static bool Aligned(string a, string b) => a == b || a.EndsWith("." + b, StringComparison.Ordinal) || b.EndsWith("." + a, StringComparison.Ordinal);

    /// <summary>Exact domain or a real subdomain; "evilhdfcbank.net" and "hdfcbank.net.evil.com" do not match "hdfcbank.net".</summary>
    private static bool IsAllowed(string domain, IReadOnlyList<string> allowed) =>
        allowed.Any(a => domain == a || domain.EndsWith("." + a, StringComparison.Ordinal));

    private static string? DomainOf(string? from)
    {
        if (string.IsNullOrWhiteSpace(from)) return null;
        var start = from.LastIndexOf('<');
        var address = start >= 0 ? from[(start + 1)..].TrimEnd('>', ' ') : from.Trim();
        var at = address.LastIndexOf('@');
        return at < 0 || at == address.Length - 1 ? null : address[(at + 1)..].Trim().ToLowerInvariant();
    }

    private static string? Property(string clause, string name)
    {
        var i = clause.IndexOf(name + "=", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return null;
        var value = clause[(i + name.Length + 1)..].Split(' ', ';', ')')[0].Trim('"');
        return value.Length == 0 ? null : value;
    }
}
