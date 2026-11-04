namespace PennyPath.Application.Account;

/// <summary>The part that actually talks to a mail server; behind an interface so tests never send mail.</summary>
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}
