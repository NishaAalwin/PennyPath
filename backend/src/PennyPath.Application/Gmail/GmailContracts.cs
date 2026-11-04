namespace PennyPath.Application.Gmail;

public record GmailStatus(bool Configured, bool Connected, DateTime? ConnectedAt, DateTime? LastSyncAt);

/// <param name="Checked">Emails read this time (already imported ones are skipped before reading).</param>
/// <param name="NotRecognisedSubjects">Subjects (shortened) of emails that looked like alerts but could not be read; shown once, never stored.</param>
/// <param name="MoreAvailable">More matching emails exist than one sync reads; sync again.</param>
public record GmailSyncResult(
    int Checked, int Imported, int Duplicates, int PossibleDuplicates, int NotRecognised,
    IReadOnlyList<string> NotRecognisedSubjects, bool MoreAvailable);

public enum GmailSyncStatus { Ok, NotConfigured, NotConnected, NeedsReconnect, Busy, Failed }

public record GmailSyncOutcome(GmailSyncStatus Status, GmailSyncResult? Result = null);

/// <summary>One email, already decoded to text. Held in memory while it is parsed and then dropped.</summary>
public record EmailItem(string Id, string Subject, string Body, DateOnly ReceivedOn);

public interface IGmailService
{
    Task<GmailStatus> StatusAsync(Guid userId, CancellationToken ct = default);

    /// <summary>The Google consent URL to send the user to, or null when Gmail is not configured on this server.</summary>
    Task<string?> StartConnectAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Finishes the consent. Returns false for any failure; the reason is deliberately not revealed.</summary>
    Task<bool> CompleteConnectAsync(string? state, string? code, string? error, string? ip, CancellationToken ct = default);

    Task<GmailSyncOutcome> SyncAsync(Guid userId, string? ip, CancellationToken ct = default);

    /// <summary>Revokes the grant at Google (best effort) and deletes the stored token.</summary>
    Task DisconnectAsync(Guid userId, string? ip, CancellationToken ct = default);
}

public record GoogleTokens(string? RefreshToken, string Scope);

/// <summary>A token Google no longer accepts (revoked, expired in test mode, or password changed).</summary>
public class GoogleAuthException(string message) : Exception(message);

/// <summary>The network side of Google, behind an interface so everything else can be tested without Google.</summary>
public interface IGoogleApi
{
    bool IsConfigured { get; }
    string BuildAuthorizationUrl(string state, string codeChallenge);
    Task<GoogleTokens> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct);

    /// <exception cref="GoogleAuthException">The refresh token is no longer valid.</exception>
    Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct);

    /// <returns>Matching message ids, newest first, at most <paramref name="max"/>, and whether more exist.</returns>
    Task<(IReadOnlyList<string> Ids, bool More)> SearchMessageIdsAsync(string accessToken, string query, int max, CancellationToken ct);

    Task<EmailItem?> GetMessageAsync(string accessToken, string id, CancellationToken ct);
    Task RevokeAsync(string token, CancellationToken ct);
}
