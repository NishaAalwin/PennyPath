using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PennyPath.Application.Gmail;
using PennyPath.Application.Security;
using PennyPath.Application.Transactions;
using PennyPath.Domain.Entities;
using PennyPath.Infrastructure.Persistence;
using PennyPath.Infrastructure.Transactions;

namespace PennyPath.Infrastructure.Gmail;

public class GmailService(
    AppDbContext db, IGoogleApi google, ITokenProtector protector, ITransactionService transactions,
    IAuditLog audit, TimeProvider clock, ILogger<GmailService> log) : IGmailService
{
    public const int MaxPerSync = 300;
    public const int FetchConcurrency = 4;
    public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    // One sync per user at a time, so a double click cannot read the mailbox twice.
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Gates = new();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<GmailStatus> StatusAsync(Guid userId, CancellationToken ct = default)
    {
        var c = await db.GmailConnections.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        return new GmailStatus(google.IsConfigured, c is not null, c?.ConnectedAt, c?.LastSyncAt);
    }

    public async Task<string?> StartConnectAsync(Guid userId, CancellationToken ct = default)
    {
        if (!google.IsConfigured) return null;

        var now = Now;
        // Old or abandoned requests are removed; a user has at most one pending request.
        db.GmailOAuthStates.RemoveRange(await db.GmailOAuthStates.Where(x => x.ExpiresAt < now || x.UserId == userId).ToListAsync(ct));

        var state = RandomToken();
        var verifier = RandomToken();
        db.GmailOAuthStates.Add(new GmailOAuthState
        {
            UserId = userId,
            StateHash = Sha256Hex(state),
            ProtectedCodeVerifier = protector.Protect(verifier),
            ExpiresAt = now + StateLifetime,
        });
        await db.SaveChangesAsync(ct);

        return google.BuildAuthorizationUrl(state, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public async Task<bool> CompleteConnectAsync(string? state, string? code, string? error, string? ip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(state) || state.Length > 200) return false;

        // The state is single-use: it is deleted before anything else happens, whatever the outcome.
        var hash = Sha256Hex(state);
        var pending = await db.GmailOAuthStates.FirstOrDefaultAsync(x => x.StateHash == hash, ct);
        if (pending is null) return false;
        db.GmailOAuthStates.Remove(pending);
        await db.SaveChangesAsync(ct);

        if (pending.ExpiresAt < Now || !string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code) || code.Length > 2000) return false;
        var verifier = protector.Unprotect(pending.ProtectedCodeVerifier);
        if (verifier is null) return false;

        GoogleTokens tokens;
        try
        {
            tokens = await google.ExchangeCodeAsync(code, verifier, ct);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct) || ex is GoogleAuthException)
        {
            log.LogWarning("Gmail code exchange failed ({Kind})", ex.GetType().Name);
            return false;
        }

        // Only read access is acceptable. If Google reports anything else, do not keep the grant.
        var scopes = tokens.Scope.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (string.IsNullOrEmpty(tokens.RefreshToken) || scopes.Length != 1 || scopes[0] != GmailQuery.ReadOnlyScope)
        {
            if (!string.IsNullOrEmpty(tokens.RefreshToken)) await TryRevokeAsync(tokens.RefreshToken, ct);
            return false;
        }

        var existing = await db.GmailConnections.FirstOrDefaultAsync(x => x.UserId == pending.UserId, ct);
        if (existing is not null)
        {
            var old = protector.Unprotect(existing.ProtectedRefreshToken);
            if (old is not null) await TryRevokeAsync(old, ct);
            db.GmailConnections.Remove(existing);
        }

        db.GmailConnections.Add(new GmailConnection
        {
            UserId = pending.UserId,
            ProtectedRefreshToken = protector.Protect(tokens.RefreshToken),
            ConnectedAt = Now,
        });
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(pending.UserId, AuditEventTypes.GmailConnected, ip, ct);
        return true;
    }

    public async Task<GmailSyncOutcome> SyncAsync(Guid userId, string? ip, CancellationToken ct = default)
    {
        if (!google.IsConfigured) return new(GmailSyncStatus.NotConfigured);
        var conn = await db.GmailConnections.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (conn is null) return new(GmailSyncStatus.NotConnected);

        var gate = Gates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct)) return new(GmailSyncStatus.Busy);
        try
        {
            var refresh = protector.Unprotect(conn.ProtectedRefreshToken);
            if (refresh is null) return await NeedsReconnectAsync(conn, ip, ct); // key ring lost or value damaged

            try
            {
                var access = await google.RefreshAccessTokenAsync(refresh, ct);
                var started = Now;

                var (ids, more) = await google.SearchMessageIdsAsync(access, GmailQuery.Build(conn.LastSyncAt, started), MaxPerSync, ct);

                // Emails already imported are recognised by id and never read again.
                var keys = ids.Distinct().ToDictionary(TransactionService.EmailKey);
                var keyList = keys.Keys.ToList();
                var known = (await db.Transactions.Where(t => t.UserId == userId && keyList.Contains(t.RawHash)).Select(t => t.RawHash).ToListAsync(ct)).ToHashSet();
                var toRead = keys.Where(k => !known.Contains(k.Key)).Select(k => k.Value).ToList();

                var items = await ReadAsync(access, toRead, ct);
                var imported = await transactions.ImportEmailsAsync(userId, items, ct);

                // If the cap cut the list short, keep the old marker so the next sync carries on from the same point.
                if (!more) conn.LastSyncAt = started;
                await db.SaveChangesAsync(ct);
                await audit.RecordAsync(userId, AuditEventTypes.GmailSynced, ip, ct);

                return new(GmailSyncStatus.Ok, new GmailSyncResult(
                    items.Count, imported.Imported, known.Count + imported.Duplicates, imported.PossibleDuplicates,
                    imported.NotRecognisedSubjects.Count, imported.NotRecognisedSubjects.Take(20).ToList(), more));
            }
            catch (GoogleAuthException)
            {
                return await NeedsReconnectAsync(conn, ip, ct);
            }
            catch (Exception ex) when (IsNetworkFailure(ex, ct))
            {
                log.LogWarning("Gmail sync failed ({Kind})", ex.GetType().Name);
                return new(GmailSyncStatus.Failed);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task DisconnectAsync(Guid userId, string? ip, CancellationToken ct = default)
    {
        var conn = await db.GmailConnections.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        db.GmailOAuthStates.RemoveRange(await db.GmailOAuthStates.Where(x => x.UserId == userId).ToListAsync(ct));
        if (conn is null)
        {
            await db.SaveChangesAsync(ct);
            return;
        }

        // The stored token goes whether or not Google could be reached; revoking is a courtesy on top.
        var token = protector.Unprotect(conn.ProtectedRefreshToken);
        db.GmailConnections.Remove(conn);
        await db.SaveChangesAsync(ct);
        if (token is not null) await TryRevokeAsync(token, ct);
        await audit.RecordAsync(userId, AuditEventTypes.GmailDisconnected, ip, ct);
    }

    private async Task<List<EmailItem>> ReadAsync(string access, IReadOnlyList<string> ids, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(FetchConcurrency);
        var tasks = ids.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try
            {
                return await google.GetMessageAsync(access, id, ct);
            }
            finally
            {
                gate.Release();
            }
        });
        return (await Task.WhenAll(tasks)).OfType<EmailItem>().ToList();
    }

    private async Task<GmailSyncOutcome> NeedsReconnectAsync(GmailConnection conn, string? ip, CancellationToken ct)
    {
        db.GmailConnections.Remove(conn);
        await db.SaveChangesAsync(ct);
        await audit.RecordAsync(conn.UserId, AuditEventTypes.GmailNeedsReconnect, ip, ct);
        return new(GmailSyncStatus.NeedsReconnect);
    }

    private async Task TryRevokeAsync(string token, CancellationToken ct)
    {
        try
        {
            await google.RevokeAsync(token, ct);
        }
        catch (Exception ex) when (IsNetworkFailure(ex, ct))
        {
            log.LogWarning("Could not revoke the Gmail grant at Google ({Kind})", ex.GetType().Name);
        }
    }

    private static bool IsNetworkFailure(Exception ex, CancellationToken ct) =>
        !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException;

    private static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
