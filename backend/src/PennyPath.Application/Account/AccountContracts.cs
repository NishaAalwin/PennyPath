using PennyPath.Application.Transactions;

namespace PennyPath.Application.Account;

public record EmailMessage(string To, string Subject, string Body);

/// <summary>Sends an email. Sending must never block or fail a request: the real implementation queues the message.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public record AccountInfo(string Email, bool EmailVerified, bool MfaEnabled, bool GmailConnected);

public enum ResetOutcome { Done, InvalidToken, WeakPassword }
public record ResetResult(ResetOutcome Outcome, string? Message = null);

public enum DeleteOutcome { Deleted, NotAuthenticated, LockedOut, MfaRequired }

public record MfaSetup(string Secret, string OtpAuthUri);
public record MfaEnableResult(bool Enabled, IReadOnlyList<string> RecoveryCodes);

public record ExportedEvent(string Type, DateTime At);
public record AccountExport(
    DateTime ExportedAt, string Email, DateTime CreatedAt, bool EmailVerified, bool MfaEnabled, bool AiConsent, bool GmailConnected,
    IReadOnlyList<TransactionDto> Transactions, IReadOnlyList<ExportedEvent> SecurityActivity);

public interface IAccountService
{
    Task<AccountInfo?> InfoAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Emails a fresh verification link. Quietly does nothing when already verified or when asked too often.</summary>
    Task SendVerificationAsync(Guid userId, CancellationToken ct = default);

    /// <returns>True when the token was valid, unused and unexpired; the account is then verified.</returns>
    Task<bool> VerifyEmailAsync(string? token, string? ip, CancellationToken ct = default);

    /// <summary>Emails a reset link if the address belongs to an account. Always completes the same way, so it reveals nothing.</summary>
    Task RequestPasswordResetAsync(string? email, string? ip, CancellationToken ct = default);

    /// <summary>Sets a new password, ends every session and clears any lockout. The token works once.</summary>
    Task<ResetResult> ResetPasswordAsync(string? token, string? newPassword, string? ip, CancellationToken ct = default);

    /// <summary>Deletes the account and everything that belongs to it, after the password (and code) is checked again.</summary>
    Task<DeleteOutcome> DeleteAccountAsync(Guid userId, string? password, string? code, string? ip, CancellationToken ct = default);

    Task<AccountExport?> ExportAsync(Guid userId, string? ip, CancellationToken ct = default);

    /// <summary>Starts two-factor setup. Null when it is already on.</summary>
    Task<MfaSetup?> BeginMfaSetupAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Turns two-factor on if the code from the authenticator app is right; returns the one-time recovery codes.</summary>
    Task<MfaEnableResult> EnableMfaAsync(Guid userId, string? code, string? ip, CancellationToken ct = default);

    /// <returns>False when the password or code is wrong.</returns>
    Task<bool> DisableMfaAsync(Guid userId, string? password, string? code, string? ip, CancellationToken ct = default);
}

/// <summary>Checks a two-factor code (or recovery code) for a user and records that it was used.</summary>
public interface IMfaVerifier
{
    Task<bool> VerifyAsync(Guid userId, string? code, CancellationToken ct = default);
}
