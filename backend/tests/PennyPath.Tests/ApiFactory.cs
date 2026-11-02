using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PennyPath.Tests;

/// <summary>API host for tests that must not need a database (no startup migration).</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("Database:MigrateOnStartup", "false");
}
