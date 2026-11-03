using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PennyPath.Infrastructure.Persistence;

namespace PennyPath.Tests;

/// <summary>The real API over an in-memory database, so sign-in, sessions and the audit log can be tested end to end.</summary>
public sealed class DbApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            // Drop the Npgsql registration (DbContextOptions<AppDbContext> and EF's options-configuration for it).
            foreach (var d in services.Where(d => d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(AppDbContext))).ToList())
                services.Remove(d);
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
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
