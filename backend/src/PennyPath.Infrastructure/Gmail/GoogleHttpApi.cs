using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PennyPath.Application.Gmail;
using PennyPath.Application.Parsing;

namespace PennyPath.Infrastructure.Gmail;

public class GoogleOptions
{
    public const string Section = "Google";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
}

/// <summary>Talks to Google's OAuth and Gmail endpoints. Secrets only ever travel in POST bodies or the Authorization header, never in a URL.</summary>
public class GoogleHttpApi(HttpClient http, IOptions<GoogleOptions> options) : IGoogleApi
{
    private const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl = "https://oauth2.googleapis.com/token";
    private const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    private const string GmailBase = "https://gmail.googleapis.com/gmail/v1/users/me";
    private const int MaxBodyChars = 20_000;
    private const int PageSize = 100;

    private readonly GoogleOptions _o = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_o.ClientId) && !string.IsNullOrWhiteSpace(_o.ClientSecret)
        && Uri.TryCreate(_o.RedirectUri, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = _o.ClientId,
            ["redirect_uri"] = _o.RedirectUri,
            ["response_type"] = "code",
            ["scope"] = GmailQuery.ReadOnlyScope, // read-only, nothing else
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["access_type"] = "offline",          // so Google issues a refresh token
            ["prompt"] = "consent",               // and issues it again when the user reconnects
            ["include_granted_scopes"] = "false",
        };
        return AuthUrl + "?" + string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<GoogleTokens> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        using var doc = await PostFormAsync(TokenUrl, new()
        {
            ["code"] = code,
            ["client_id"] = _o.ClientId,
            ["client_secret"] = _o.ClientSecret,
            ["redirect_uri"] = _o.RedirectUri,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = codeVerifier,
        }, ct);

        var root = doc.RootElement;
        return new GoogleTokens(
            root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null,
            root.TryGetProperty("scope", out var s) ? s.GetString() ?? string.Empty : string.Empty);
    }

    public async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        using var doc = await PostFormAsync(TokenUrl, new()
        {
            ["client_id"] = _o.ClientId,
            ["client_secret"] = _o.ClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        }, ct);
        return doc.RootElement.GetProperty("access_token").GetString() ?? throw new JsonException("No access token.");
    }

    public async Task<(IReadOnlyList<string> Ids, bool More)> SearchMessageIdsAsync(string accessToken, string query, int max, CancellationToken ct)
    {
        var ids = new List<string>();
        string? page = null;
        var more = false;
        while (true)
        {
            // Ask for one more than the cap so we can tell whether anything is left over.
            var url = $"{GmailBase}/messages?q={Uri.EscapeDataString(query)}&maxResults={Math.Min(PageSize, max - ids.Count + 1)}"
                      + (page is null ? "" : $"&pageToken={Uri.EscapeDataString(page)}");
            using var doc = await GetJsonAsync(url, accessToken, ct);
            if (doc.RootElement.TryGetProperty("messages", out var list))
                ids.AddRange(list.EnumerateArray().Select(m => m.GetProperty("id").GetString()!).Where(id => !string.IsNullOrEmpty(id)));
            page = doc.RootElement.TryGetProperty("nextPageToken", out var n) ? n.GetString() : null;

            if (ids.Count > max)
            {
                more = true;
                ids.RemoveRange(max, ids.Count - max);
                break;
            }
            if (page is null) break;
        }

        return (ids, more);
    }

    public async Task<EmailItem?> GetMessageAsync(string accessToken, string id, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"{GmailBase}/messages/{Uri.EscapeDataString(id)}?format=full", accessToken, ct, allowNotFound: true);
        if (doc is null) return null;

        var root = doc.RootElement;
        var subject = string.Empty;
        string? from = null;
        var auth = new List<string>();
        if (root.TryGetProperty("payload", out var payload))
        {
            if (payload.TryGetProperty("headers", out var headers))
                foreach (var h in headers.EnumerateArray())
                {
                    var name = h.GetProperty("name").GetString();
                    var value = h.GetProperty("value").GetString() ?? string.Empty;
                    if (string.Equals(name, "Subject", StringComparison.OrdinalIgnoreCase)) subject = value;
                    else if (string.Equals(name, "From", StringComparison.OrdinalIgnoreCase)) from = value;
                    else if (string.Equals(name, "Authentication-Results", StringComparison.OrdinalIgnoreCase)) auth.Add(value);
                }
        }

        var body = root.TryGetProperty("payload", out var p) ? ExtractBody(p) : string.Empty;
        if (body.Length > MaxBodyChars) body = body[..MaxBodyChars];

        // India is the assumed market, so "the day it arrived" is the day in IST.
        var ms = root.TryGetProperty("internalDate", out var d) && long.TryParse(d.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var received = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.AddHours(5.5));
        return new EmailItem(id, subject, body, received, from, auth);
    }

    public async Task RevokeAsync(string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, RevokeUrl) { Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token }) };
        using var res = await http.SendAsync(req, ct);
        // 200 = revoked, 400 = already invalid: both mean the grant is gone.
        if (res.StatusCode is not (HttpStatusCode.OK or HttpStatusCode.BadRequest)) res.EnsureSuccessStatusCode();
    }

    /// <summary>Prefers the plain-text part; falls back to HTML reduced to text.</summary>
    internal static string ExtractBody(JsonElement part)
    {
        var plain = new StringBuilder();
        var html = new StringBuilder();
        Walk(part, plain, html);
        return plain.Length > 0 ? plain.ToString() : EmailTextExtractor.ToPlainText(html.ToString());
    }

    private static void Walk(JsonElement part, StringBuilder plain, StringBuilder html)
    {
        var mime = part.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
        if (part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } enc)
        {
            var text = DecodeBase64Url(enc);
            if (mime == "text/plain") plain.Append(text).Append(' ');
            else if (mime == "text/html") html.Append(text).Append(' ');
        }

        if (part.TryGetProperty("parts", out var parts))
            foreach (var child in parts.EnumerateArray()) Walk(child, plain, html);
    }

    private static string DecodeBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }

    private async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        using var res = await http.PostAsync(url, new FormUrlEncodedContent(form), ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            // Google answers 400 invalid_grant when a refresh token was revoked or has expired (7 days in test mode).
            if (res.StatusCode == HttpStatusCode.BadRequest && text.Contains("invalid_grant", StringComparison.Ordinal))
                throw new GoogleAuthException("Google no longer accepts this grant.");
            throw new HttpRequestException($"Google token endpoint answered {(int)res.StatusCode}.", null, res.StatusCode);
        }
        return JsonDocument.Parse(text);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, string accessToken, CancellationToken ct) =>
        (await GetJsonAsync(url, accessToken, ct, allowNotFound: false))!;

    private async Task<JsonDocument?> GetJsonAsync(string url, string accessToken, CancellationToken ct, bool allowNotFound)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var res = await http.SendAsync(req, ct);
        if (allowNotFound && res.StatusCode == HttpStatusCode.NotFound) return null; // deleted between listing and reading
        if (res.StatusCode == HttpStatusCode.Unauthorized) throw new GoogleAuthException("Google no longer accepts this access token.");
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
    }
}
