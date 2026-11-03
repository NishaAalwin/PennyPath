using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PennyPath.Application.Auth;
using PennyPath.Infrastructure.Auth;

namespace PennyPath.Tests;

// These tests never touch the database: /me only validates the JWT.
public class AuthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Me_without_token_is_unauthorized()
    {
        var res = await factory.CreateClient().GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/transactions")]
    [InlineData("POST", "/api/sms/import")]
    [InlineData("GET", "/api/categories")]
    [InlineData("GET", "/api/summary")]
    [InlineData("DELETE", "/api/transactions/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("POST", "/api/transactions/3fa85f64-5717-4562-b3fc-2c963f66afa6/not-duplicate")]
    [InlineData("PUT", "/api/transactions/3fa85f64-5717-4562-b3fc-2c963f66afa6/transfer")]
    [InlineData("PUT", "/api/transactions/3fa85f64-5717-4562-b3fc-2c963f66afa6/category")]
    public async Task Transaction_endpoints_require_authentication(string method, string url)
    {
        var res = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "POST" ? JsonContent.Create(new { messages = new[] { "x" } }) : null,
        });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Me_with_valid_token_returns_email()
    {
        var token = factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope()
            .ServiceProvider.GetRequiredService<ITokenService>().Create(Guid.NewGuid(), "a@b.com").Token;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("a@b.com", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_no_server_banner()
    {
        var res = await factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store", res.Headers.GetValues("Cache-Control").Single());
        Assert.Contains("default-src 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.False(res.Headers.Contains("Server"));
    }

    [Fact]
    public async Task Unsigned_none_algorithm_token_is_rejected()
    {
        static string B64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var none = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"PennyPath\",\"aud\":\"PennyPath\",\"exp\":{exp}}}")}.";
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", none);

        var res = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Oversized_request_body_is_rejected()
    {
        var big = new StringContent(new string('a', 1_100_000), System.Text.Encoding.UTF8, "application/json");

        var res = await factory.CreateClient().PostAsync("/api/auth/login", big);

        Assert.True(res.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_with_token_signed_by_another_key_is_unauthorized()
    {
        var forged = new JwtTokenService(Options.Create(new JwtOptions { Key = new string('x', 40) }))
            .Create(Guid.NewGuid(), "a@b.com").Token;
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var res = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
