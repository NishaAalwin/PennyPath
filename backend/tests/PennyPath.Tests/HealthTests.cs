using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PennyPath.Tests;

public class HealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_responds_degraded_when_database_is_unreachable()
    {
        var client = factory.WithWebHostBuilder(b => b.UseSetting(
            "ConnectionStrings:Default",
            "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1")).CreateClient();

        var res = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("\"status\":\"degraded\"", await res.Content.ReadAsStringAsync());
    }
}
