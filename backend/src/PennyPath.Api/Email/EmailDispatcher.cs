using PennyPath.Application.Account;
using PennyPath.Infrastructure.Email;

namespace PennyPath.Api.Email;

/// <summary>Sends queued mail one at a time. A failure is logged by type only (never the message, which holds a link) and does not stop the queue.</summary>
public sealed class EmailDispatcher(QueuedEmailSender queue, IEmailTransport transport, ILogger<EmailDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await transport.SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Sending an email failed ({Kind})", ex.GetType().Name);
            }
        }
    }
}
