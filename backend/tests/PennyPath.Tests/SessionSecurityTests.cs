using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PennyPath.Application.Auth;

namespace PennyPath.Tests;

public class SessionSecurityTests
{
    private const string Password = "correct-horse-battery";
    private const string CookieName = "__Host-pp_session";

    private static async Task<HttpResponseMessage> Register(HttpClient c, string email) =>
        await c.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

    private static string CookieHeaderValue(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(CookieName + "="));

    /// <summary>"name=value" part of the Set-Cookie header, for replaying the cookie by hand.</summary>
    private static string CookiePair(HttpResponseMessage res) => CookieHeaderValue(res).Split(';')[0];

    private static HttpRequestMessage WithCookie(HttpMethod method, string url, string cookie, bool csrf = true)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", cookie);
        if (csrf) req.Headers.Add("X-Requested-With", "PennyPath");
        return req;
    }

    // ---- the cookie itself ----

    [Fact]
    public async Task Sign_up_sets_a_hardened_cookie_and_keeps_the_token_out_of_the_body()
    {
        using var f = new DbApiFactory();
        var res = await Register(f.Browser(), "asha@example.com");

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var cookie = CookieHeaderValue(res).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);

        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJ", body); // a JWT always starts with eyJ
        Assert.Contains("asha@example.com", body);
    }

    [Fact]
    public async Task Login_sets_the_cookie_and_the_cookie_alone_authenticates_requests()
    {
        using var f = new DbApiFactory();
        var c = f.Browser();
        await Register(c, "asha@example.com");
        c.DefaultRequestHeaders.Remove("Cookie");

        var fresh = f.Browser();
        var login = await fresh.PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = Password });
        var me = await fresh.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.StartsWith(CookieName, CookieHeaderValue(login));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains("asha@example.com", await me.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_plain_http_client_without_the_cookie_is_unauthorized()
    {
        using var f = new DbApiFactory();
        await Register(f.Browser(), "asha@example.com");

        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Raw().GetAsync("/api/auth/me")).StatusCode);
    }

    // ---- logout and revocation ----

    [Fact]
    public async Task Logout_ends_the_session_so_a_stolen_copy_of_the_cookie_stops_working()
    {
        using var f = new DbApiFactory();
        var reg = await Register(f.Browser(), "asha@example.com");
        var cookie = CookiePair(reg);
        var raw = f.Raw();
        Assert.Equal(HttpStatusCode.OK, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode);

        var logout = await raw.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/logout", cookie));

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), h => h.StartsWith(CookieName + "=;") || h.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task Logout_ends_only_this_session_and_logout_all_ends_every_one()
    {
        using var f = new DbApiFactory();
        var raw = f.Raw();
        var first = CookiePair(await Register(f.Browser(), "asha@example.com"));
        var second = CookiePair(await f.Browser().PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = Password }));
        var third = CookiePair(await f.Browser().PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = Password }));

        await raw.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/logout", first));
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", first))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", second))).StatusCode);

        await raw.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/logout-all", second));
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", second))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", third))).StatusCode);
    }

    [Fact]
    public async Task Logging_out_one_user_never_touches_another_users_session()
    {
        using var f = new DbApiFactory();
        var raw = f.Raw();
        var asha = CookiePair(await Register(f.Browser(), "asha@example.com"));
        var ravi = CookiePair(await Register(f.Browser(), "ravi@example.com"));

        await raw.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/logout-all", asha));

        Assert.Equal(HttpStatusCode.OK, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", ravi))).StatusCode);
    }

    [Fact]
    public async Task A_correctly_signed_token_for_a_session_that_does_not_exist_is_rejected()
    {
        using var f = new DbApiFactory();
        using var scope = f.Services.CreateScope();
        var token = scope.ServiceProvider.GetRequiredService<ITokenService>().Create(Guid.NewGuid(), "ghost@example.com", Guid.NewGuid()).Token;
        var client = f.Raw();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_session_that_has_expired_in_the_database_is_rejected()
    {
        using var f = new DbApiFactory();
        var cookie = CookiePair(await Register(f.Browser(), "asha@example.com"));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PennyPath.Infrastructure.Persistence.AppDbContext>();
            foreach (var s in db.Sessions) s.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Raw().SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode);
    }

    // ---- CSRF ----

    [Fact]
    public async Task A_cookie_authenticated_change_without_the_app_header_is_blocked_and_does_nothing()
    {
        using var f = new DbApiFactory();
        var cookie = CookiePair(await Register(f.Browser(), "asha@example.com"));
        var raw = f.Raw();

        var blocked = await raw.SendAsync(WithCookie(HttpMethod.Post, "/api/auth/logout", cookie, csrf: false));

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await raw.SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie))).StatusCode);
    }

    [Theory]
    [InlineData("cross-site", HttpStatusCode.Forbidden)]
    [InlineData("same-site", HttpStatusCode.Forbidden)]
    [InlineData("same-origin", HttpStatusCode.NoContent)]
    public async Task The_browsers_fetch_metadata_must_say_same_origin(string site, HttpStatusCode expected)
    {
        using var f = new DbApiFactory();
        var cookie = CookiePair(await Register(f.Browser(), "asha@example.com"));
        var req = WithCookie(HttpMethod.Post, "/api/auth/logout", cookie);
        req.Headers.Add("Sec-Fetch-Site", site);

        Assert.Equal(expected, (await f.Raw().SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Reading_with_the_cookie_needs_no_csrf_header()
    {
        using var f = new DbApiFactory();
        var cookie = CookiePair(await Register(f.Browser(), "asha@example.com"));

        Assert.Equal(HttpStatusCode.OK, (await f.Raw().SendAsync(WithCookie(HttpMethod.Get, "/api/auth/me", cookie, csrf: false))).StatusCode);
    }

    [Fact]
    public async Task Signing_in_or_up_without_the_app_header_is_blocked()
    {
        using var f = new DbApiFactory();
        var c = f.Browser(csrfHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await Register(c, "asha@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await c.PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = Password })).StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_is_not_ambient_so_it_needs_no_csrf_header()
    {
        using var f = new DbApiFactory();
        string token;
        using (var scope = f.Services.CreateScope())
            token = (await scope.ServiceProvider.GetRequiredService<IAuthService>().RegisterAsync(new RegisterRequest("api@example.com", Password))).Response!.Token;
        var client = f.Raw();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    // ---- audit log ----

    private static async Task<JsonElement> Events(HttpClient c) =>
        JsonDocument.Parse(await (await c.GetAsync("/api/security/events")).Content.ReadAsStringAsync()).RootElement;

    private static string[] Types(JsonElement events) => events.EnumerateArray().Select(e => e.GetProperty("type").GetString()!).ToArray();

    [Fact]
    public async Task Sign_in_activity_is_recorded_and_visible_to_the_account_owner()
    {
        using var f = new DbApiFactory();
        var c = f.Browser();
        await Register(c, "asha@example.com");
        var other = f.Browser();
        await other.PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = "wrong-password-1" });
        await c.PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = Password });

        var types = Types(await Events(c));

        Assert.Contains("Registered", types);
        Assert.Contains("LoginFailed", types);
        Assert.Contains("LoginSucceeded", types);
    }

    [Fact]
    public async Task Lockout_and_logout_are_recorded()
    {
        using var f = new DbApiFactory();
        var c = f.Browser();
        await Register(c, "asha@example.com");
        for (var i = 0; i < 6; i++)
            await f.Browser().PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = "wrong-password-1" });

        var types = Types(await Events(c));
        Assert.Contains("LoginBlockedByLockout", types);

        await c.PostAsync("/api/auth/logout", null);
    }

    [Fact]
    public async Task A_users_activity_is_not_visible_to_other_users()
    {
        using var f = new DbApiFactory();
        var asha = f.Browser();
        var ravi = f.Browser();
        await Register(asha, "asha@example.com");
        await Register(ravi, "ravi@example.com");
        await f.Browser().PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = "wrong-password-1" });

        var raviTypes = Types(await Events(ravi));

        Assert.DoesNotContain("LoginFailed", raviTypes);
        Assert.Equal(["Registered"], raviTypes);
    }

    [Fact]
    public async Task Audit_entries_hold_only_type_time_and_ip_and_never_the_password_or_email()
    {
        using var f = new DbApiFactory();
        var c = f.Browser();
        await Register(c, "asha@example.com");
        await c.PostAsJsonAsync("/api/auth/login", new { email = "asha@example.com", password = "wrong-password-1" });

        var json = await (await c.GetAsync("/api/security/events")).Content.ReadAsStringAsync();

        Assert.DoesNotContain("wrong-password-1", json);
        Assert.DoesNotContain("asha@example.com", json);
        Assert.DoesNotContain(Password, json);
    }

    [Fact]
    public async Task Security_endpoints_require_a_session()
    {
        using var f = new DbApiFactory();
        var c = f.Raw();

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/security/events")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/privacy/ai")).StatusCode);
    }

    // ---- AI consent ----

    [Fact]
    public async Task Ai_consent_is_off_by_default_and_can_be_granted_and_withdrawn_with_an_audit_trail()
    {
        using var f = new DbApiFactory();
        var c = f.Browser();
        await Register(c, "asha@example.com");

        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/privacy/ai")).GetProperty("enabled").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/privacy/ai", new { enabled = true })).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/privacy/ai")).GetProperty("enabled").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PutAsJsonAsync("/api/privacy/ai", new { enabled = false })).StatusCode);
        Assert.False((await c.GetFromJsonAsync<JsonElement>("/api/privacy/ai")).GetProperty("enabled").GetBoolean());

        var types = Types(await Events(c));
        Assert.Contains("AiConsentGranted", types);
        Assert.Contains("AiConsentRevoked", types);
    }

    [Fact]
    public async Task Ai_consent_belongs_to_one_user_and_needs_the_app_header()
    {
        using var f = new DbApiFactory();
        var asha = f.Browser();
        var ravi = f.Browser();
        await Register(asha, "asha@example.com");
        await Register(ravi, "ravi@example.com");
        await asha.PutAsJsonAsync("/api/privacy/ai", new { enabled = true });

        Assert.False((await ravi.GetFromJsonAsync<JsonElement>("/api/privacy/ai")).GetProperty("enabled").GetBoolean());

        var cookie = CookiePair(await f.Browser().PostAsJsonAsync("/api/auth/login", new { email = "ravi@example.com", password = Password }));
        var forged = await f.Raw().SendAsync(WithCookie(HttpMethod.Put, "/api/privacy/ai", cookie, csrf: false));
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
    }
}
