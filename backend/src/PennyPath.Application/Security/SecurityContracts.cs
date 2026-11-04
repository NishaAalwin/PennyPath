namespace PennyPath.Application.Security;

public static class AuditEventTypes
{
    public const string Registered = "Registered";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LoginBlockedByLockout = "LoginBlockedByLockout";
    public const string LoggedOut = "LoggedOut";
    public const string LoggedOutEverywhere = "LoggedOutEverywhere";
    public const string AiConsentGranted = "AiConsentGranted";
    public const string AiConsentRevoked = "AiConsentRevoked";
    public const string GmailConnected = "GmailConnected";
    public const string GmailDisconnected = "GmailDisconnected";
    public const string GmailSynced = "GmailSynced";
    public const string GmailNeedsReconnect = "GmailNeedsReconnect";
    public const string EmailVerified = "EmailVerified";
    public const string PasswordResetRequested = "PasswordResetRequested";
    public const string PasswordReset = "PasswordReset";
    public const string MfaEnabled = "MfaEnabled";
    public const string MfaDisabled = "MfaDisabled";
    public const string DataExported = "DataExported";
    public const string AccountDeleted = "AccountDeleted";
}

public record AuditEventDto(Guid Id, string Type, DateTime At, string? Ip);

public interface IAuditLog
{
    /// <summary>Records an event and trims events older than the retention period.</summary>
    Task RecordAsync(Guid? userId, string type, string? ip, CancellationToken ct = default);

    /// <summary>The newest events for one account, newest first.</summary>
    Task<IReadOnlyList<AuditEventDto>> RecentAsync(Guid userId, int take = 50, CancellationToken ct = default);
}

/// <summary>
/// Consent for sending data to an AI service. Off by default. Every AI feature must ask <see cref="IsEnabledAsync"/>
/// first and may send only merchant text or monthly aggregates, never account numbers, balances or message text.
/// </summary>
public interface IAiConsent
{
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken ct = default);

    /// <returns>False when the user does not exist.</returns>
    Task<bool> SetAsync(Guid userId, bool enabled, CancellationToken ct = default);
}

/// <summary>Encrypts secrets (for example Gmail refresh tokens) before they are stored.</summary>
public interface ITokenProtector
{
    string Protect(string secret);

    /// <returns>Null when the value was tampered with, belongs to another purpose, or the key is gone.</returns>
    string? Unprotect(string protectedValue);
}
