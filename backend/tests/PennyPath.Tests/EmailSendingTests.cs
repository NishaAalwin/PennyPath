using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PennyPath.Application.Account;
using PennyPath.Infrastructure.Email;

namespace PennyPath.Tests;

public class EmailSendingTests
{
    private static readonly EmailMessage Mail = new("asha@example.com", "Hello", "Body");

    [Fact]
    public async Task The_queue_takes_a_message_and_returns_at_once()
    {
        var sender = new QueuedEmailSender();

        await sender.SendAsync(Mail);

        Assert.True(sender.Queue.Reader.TryRead(out var queued));
        Assert.Equal(Mail, queued);
    }

    [Fact]
    public async Task A_full_queue_drops_messages_instead_of_blocking_the_request()
    {
        var sender = new QueuedEmailSender();

        for (var i = 0; i < 500; i++) await sender.SendAsync(Mail);

        Assert.Equal(200, sender.Queue.Reader.Count);
    }

    [Fact]
    public async Task Without_a_mail_server_configured_nothing_is_sent_and_nothing_fails()
    {
        var transport = new SmtpEmailTransport(Options.Create(new SmtpOptions()), NullLogger<SmtpEmailTransport>.Instance);

        await transport.SendAsync(Mail, CancellationToken.None);
    }

    [Fact]
    public async Task A_mail_password_is_never_sent_without_tls()
    {
        // Port 1 on localhost refuses connections: if the transport tried to connect, this would throw.
        var options = new SmtpOptions { Host = "127.0.0.1", Port = 1, User = "user", Password = "secret", UseTls = false };
        var transport = new SmtpEmailTransport(Options.Create(options), NullLogger<SmtpEmailTransport>.Instance);

        await transport.SendAsync(Mail, CancellationToken.None);
    }

    [Fact]
    public async Task With_tls_on_it_does_try_to_connect()
    {
        var options = new SmtpOptions { Host = "127.0.0.1", Port = 1, User = "user", Password = "secret", UseTls = true };
        var transport = new SmtpEmailTransport(Options.Create(options), NullLogger<SmtpEmailTransport>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => transport.SendAsync(Mail, CancellationToken.None));
    }
}
