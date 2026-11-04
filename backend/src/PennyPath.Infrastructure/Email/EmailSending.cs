using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PennyPath.Application.Account;

namespace PennyPath.Infrastructure.Email;

public class SmtpOptions
{
    public const string Section = "Smtp";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS. Leave on for any real mail server; the local Mailpit catcher has no TLS.</summary>
    public bool UseTls { get; set; } = true;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string From { get; set; } = "PennyPath <no-reply@pennypath.local>";
}

/// <summary>Puts mail on a queue and returns at once, so a slow or broken mail server can neither slow nor reveal anything about a request.</summary>
public sealed class QueuedEmailSender : IEmailSender
{
    public Channel<EmailMessage> Queue { get; } = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.DropWrite });

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Queue.Writer.TryWrite(message);
        return Task.CompletedTask;
    }
}

public sealed class SmtpEmailTransport(IOptions<SmtpOptions> options, ILogger<SmtpEmailTransport> log) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            log.LogWarning("An email was not sent because Smtp:Host is not configured");
            return;
        }

        if (!o.UseTls && !string.IsNullOrEmpty(o.User))
        {
            // A mail password must never cross the network unencrypted.
            log.LogError("An email was not sent: Smtp:User is set but Smtp:UseTls is off. Turn TLS on.");
            return;
        }

        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.UseTls, Timeout = 20_000 };
        if (!string.IsNullOrEmpty(o.User)) client.Credentials = new NetworkCredential(o.User, o.Password);
        using var mail = new MailMessage { From = new MailAddress(o.From), Subject = message.Subject, Body = message.Body, IsBodyHtml = false };
        mail.To.Add(message.To);
        await client.SendMailAsync(mail, ct);
    }
}
