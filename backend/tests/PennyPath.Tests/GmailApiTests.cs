using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PennyPath.Application.Gmail;

namespace PennyPath.Tests;

public sealed class GmailApiTests : IDisposable
{
    private const string Password = "correct-horse-battery";

    private readonly DbApiFactory _db = new();
    private readonly FakeGoogleApi _google = new();
    private readonly WebApplicationFactory<Program> _app;

    public GmailApiTests()
    {
        _app = _db.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            foreach (var d in s.Where(d => d.ServiceType == typeof(IGoogleApi)).ToList()) s.Remove(d);
            s.AddSingleton<IGoogleApi>(_google);
        }));
    }

    public void Dispose()
    {
        _app.Dispose();
        _db.Dispose();
    }

    private HttpClient Client(bool csrf = true, bool cookies = true)
    {
        var c = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = cookies, AllowAutoRedirect = false,
        });
        if (csrf) c.DefaultRequestHeaders.Add("X-Requested-With", "PennyPath");
        return c;
    }

    private async Task<HttpClient> SignedIn(string email)
    {
        var c = Client();
        var res = await c.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return c;
    }

    /// <summary>Starts the consent as this user, then plays Google's redirect back with a client that has no cookies (as a SameSite=Strict cookie would be absent).</summary>
    private async Task<HttpResponseMessage> ConnectAsync(HttpClient user, string code = "auth-code-1")
    {
        var res = await user.PostAsync("/api/gmail/connect", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var url = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
        return await Client(csrf: false, cookies: false).GetAsync($"/api/gmail/callback?state={FakeGoogleApi.StateOf(url)}&code={code}");
    }

    [Fact]
    public async Task Every_gmail_endpoint_except_googles_return_address_needs_a_login()
    {
        var c = Client();

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/gmail")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/gmail/connect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/gmail/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.DeleteAsync("/api/gmail")).StatusCode);
    }

    [Fact]
    public async Task The_return_address_works_without_a_cookie_but_a_bad_state_goes_nowhere()
    {
        var res = await Client(cookies: false).GetAsync("/api/gmail/callback?state=forged&code=x");

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal("/?gmail=failed", res.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task The_return_address_only_ever_redirects_to_this_site()
    {
        var res = await Client(cookies: false).GetAsync("/api/gmail/callback?state=x&code=y&redirect=https://evil.example/");

        Assert.StartsWith("/", res.Headers.Location!.OriginalString);
        Assert.DoesNotContain("evil", res.Headers.Location.OriginalString);
    }

    [Fact]
    public async Task Without_google_settings_connect_and_sync_explain_that_the_server_is_not_set_up()
    {
        _google.IsConfigured = false;
        var c = await SignedIn("asha@example.com");

        var connect = await c.PostAsync("/api/gmail/connect", null);
        var sync = await c.PostAsync("/api/gmail/sync", null);
        var status = await c.GetFromJsonAsync<JsonElement>("/api/gmail");

        Assert.Equal(HttpStatusCode.Conflict, connect.StatusCode);
        Assert.Contains("not set up", await connect.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, sync.StatusCode);
        Assert.False(status.GetProperty("configured").GetBoolean());
    }

    [Fact]
    public async Task The_whole_flow_connect_sync_disconnect_and_no_secret_ever_reaches_the_browser()
    {
        var c = await SignedIn("asha@example.com");
        _google.Messages["m1"] = Emails.Debit("m1");
        _google.Messages["m2"] = Emails.Salary("m2");
        _google.Messages["m3"] = Emails.Statement("m3");

        var back = await ConnectAsync(c);
        Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
        Assert.Equal("/?gmail=connected", back.Headers.Location!.OriginalString);

        var statusBody = await (await c.GetAsync("/api/gmail")).Content.ReadAsStringAsync();
        var status = JsonDocument.Parse(statusBody).RootElement;
        Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.True(status.GetProperty("connected").GetBoolean());

        var syncRes = await c.PostAsync("/api/gmail/sync", null);
        Assert.Equal(HttpStatusCode.OK, syncRes.StatusCode);
        var syncBody = await syncRes.Content.ReadAsStringAsync();
        var sync = JsonDocument.Parse(syncBody).RootElement;
        Assert.Equal(2, sync.GetProperty("imported").GetInt32());
        Assert.Equal(1, sync.GetProperty("notRecognised").GetInt32());

        var list = await c.GetStringAsync("/api/transactions");
        Assert.Contains("\"source\":\"Email\"", list);

        // The refresh token, the access token and the state must not appear in anything the browser receives.
        foreach (var body in new[] { statusBody, syncBody, list, await back.Content.ReadAsStringAsync() })
        {
            Assert.DoesNotContain("refresh-secret", body);
            Assert.DoesNotContain("access-for-", body);
            Assert.DoesNotContain("client-secret", body);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync("/api/gmail")).StatusCode);
        var after = await c.GetFromJsonAsync<JsonElement>("/api/gmail");
        Assert.False(after.GetProperty("connected").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/gmail/sync", null)).StatusCode);
    }

    [Fact]
    public async Task Changing_requests_need_the_app_header_even_when_logged_in()
    {
        var c = await SignedIn("asha@example.com");
        await ConnectAsync(c);
        c.DefaultRequestHeaders.Remove("X-Requested-With");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/gmail/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/gmail/connect", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.DeleteAsync("/api/gmail")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/gmail")).StatusCode); // reading needs no header
    }

    [Fact]
    public async Task One_user_cannot_see_use_or_remove_anothers_connection()
    {
        var asha = await SignedIn("asha@example.com");
        await ConnectAsync(asha);
        _google.Messages["m1"] = Emails.Debit("m1");
        var ravi = await SignedIn("ravi@example.com");

        var status = await ravi.GetFromJsonAsync<JsonElement>("/api/gmail");
        var sync = await ravi.PostAsync("/api/gmail/sync", null);
        await ravi.DeleteAsync("/api/gmail");

        Assert.False(status.GetProperty("connected").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, sync.StatusCode);
        var ashaStatus = await asha.GetFromJsonAsync<JsonElement>("/api/gmail");
        Assert.True(ashaStatus.GetProperty("connected").GetBoolean());
        Assert.DoesNotContain("\"source\":\"Email\"", await ravi.GetStringAsync("/api/transactions"));
    }

    [Fact]
    public async Task A_revoked_grant_tells_the_user_to_reconnect()
    {
        var c = await SignedIn("asha@example.com");
        await ConnectAsync(c);
        _google.RefreshIsRevoked = true;

        var res = await c.PostAsync("/api/gmail/sync", null);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("reconnect").GetBoolean());
    }

    [Fact]
    public async Task A_google_outage_gives_a_plain_message_without_internals()
    {
        var c = await SignedIn("asha@example.com");
        await ConnectAsync(c);
        _google.SearchFails = new HttpRequestException("failed calling https://gmail.googleapis.com/...?access_token=abc");

        var res = await c.PostAsync("/api/gmail/sync", null);

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("googleapis", body);
        Assert.DoesNotContain("access_token", body);
    }
}
