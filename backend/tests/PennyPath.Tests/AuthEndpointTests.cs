using System.Net;
using System.Net.Http.Headers;
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
