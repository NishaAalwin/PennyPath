using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PennyPath.Application.Account;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Tests;

/// <summary>Captures emails instead of sending them, and can read the link token out of the last one.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get { lock (_sent) return _sent.ToList(); }
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        lock (_sent) _sent.Add(message);
        return Task.CompletedTask;
    }

    /// <summary>The token in the newest email to this address: the part after "#verify=" or "#reset=".</summary>
    public string? LastToken(string to, string kind) =>
        Sent.LastOrDefault(m => m.To == to && m.Body.Contains("#" + kind + "="))?.Body
            .Split("#" + kind + "=")[1].Split(['\n', ' ', '\r'])[0];
}

/// <summary>The real API over an in-memory database, so sign-in, sessions and the audit log can be tested end to end.</summary>
public sealed class DbApiFactory(bool requireVerifiedEmail = false) : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    public FakeEmailSender Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        // Most tests are not about email, so new accounts count as verified unless a test asks for the real rule.
        builder.UseSetting("Auth:RequireVerifiedEmail", requireVerifiedEmail ? "true" : "false");
        builder.UseSetting("App:PublicUrl", "https://pennypath.test");
        builder.ConfigureServices(services =>
        {
            // Drop the Npgsql registration (DbContextOptions<AppDbContext> and EF's options-configuration for it).
            foreach (var d in services.Where(d => d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext))).ToList())
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

            foreach (var d in services.Where(d => d.ServiceType == typeof(IEmailSender)).ToList()) services.Remove(d);
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    /// <summary>A browser-like client: HTTPS (so the Secure cookie is kept), cookies on, and the app's CSRF header.</summary>
    public HttpClient Browser(bool csrfHeader = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
        if (csrfHeader) client.DefaultRequestHeaders.Add("X-Requested-With", "PennyPath");
        return client;
    }

    /// <summary>A client that never stores cookies, to replay a captured cookie by hand.</summary>
    public HttpClient Raw() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
}
