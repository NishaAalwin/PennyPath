using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PennyPath.Application.Account;

namespace PennyPath.Tests;

public class AccountApiTests
{
    private const string Password = "correct-horse-battery";
    private const string NewPassword = "a-different-long-passphrase";
    private const string CookieName = "__Host-pp_session";

    private static async Task<HttpResponseMessage> Register(HttpClient c, string email) =>
        await c.PostAsJsonAsync("/api/auth/register", new { email, password = Password });

    private static string CookiePair(HttpResponseMessage res) =>
        res.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(CookieName + "=")).Split(';')[0];

    private static async Task<HttpClient> SignUp(DbApiFactory f, string email, bool verify = true)
    {
        var c = f.Browser();
        Assert.Equal(HttpStatusCode.Created, (await Register(c, email)).StatusCode);
        if (verify) await Verify(f, c, email);
        return c;
    }

    private static async Task Verify(DbApiFactory f, HttpClient c, string email)
    {
        var token = f.Emails.LastToken(email, "verify");
        Assert.NotNull(token);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode);
    }

    private static string CodeFor(string base32, int stepOffset = 0) =>
        Totp.CodeFor(Totp.FromBase32(base32)!, Totp.StepOf(DateTimeOffset.UtcNow) + stepOffset);

    // ---- email verification ----

    [Fact]
    public async Task A_new_account_can_only_see_the_verify_screen_until_it_opens_the_emailed_link()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com", verify: false);

        var me = await c.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.False(me.GetProperty("emailVerified").GetBoolean());
        foreach (var path in new[] { "/api/transactions", "/api/gmail", "/api/account", "/api/categories", "/api/security/events", "/api/privacy/ai" })
            Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/sms/import", new { messages = new[] { "x" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/account/export")).StatusCode);

        await Verify(f, c, "asha@example.com");

        // The same session works at once, because the check is made against the database on every request.
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/transactions")).StatusCode);
        Assert.True((await c.GetFromJsonAsync<JsonElement>("/api/auth/me")).GetProperty("emailVerified").GetBoolean());
    }

    [Fact]
    public async Task Sign_up_sends_the_verification_email_and_a_wrong_token_verifies_nothing()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com", verify: false);

        Assert.Single(f.Emails.Sent);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/verify-email", new { token = "not-a-token" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/verify-email", new { token = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/transactions")).StatusCode);
    }

    [Fact]
    public async Task An_unverified_user_can_ask_for_a_new_link()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com", verify: false);

        var res = await c.PostAsync("/api/auth/resend-verification", null);

        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.Equal(2, f.Emails.Sent.Count);
    }

    [Fact]
    public async Task Where_verification_is_switched_off_new_accounts_work_at_once()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: false);
        var c = await SignUp(f, "asha@example.com", verify: false);

        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/transactions")).StatusCode);
        Assert.Empty(f.Emails.Sent);
    }

    // ---- password reset ----

    [Fact]
    public async Task The_anonymous_account_endpoints_need_the_app_header()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = f.Browser(csrfHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/auth/forgot-password", new { email = "a@b.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/auth/reset-password", new { token = "x", newPassword = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/auth/verify-email", new { token = "x" })).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_answers_identically_for_known_and_unknown_addresses()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        await SignUp(f, "asha@example.com");
        var c = f.Browser();

        var known = await c.PostAsJsonAsync("/api/auth/forgot-password", new { email = "asha@example.com" });
        var unknown = await c.PostAsJsonAsync("/api/auth/forgot-password", new { email = "nobody@example.com" });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.DoesNotContain(f.Emails.Sent, m => m.To == "nobody@example.com");
        Assert.NotNull(f.Emails.LastToken("asha@example.com", "reset"));
    }

    [Fact]
    public async Task A_reset_over_http_changes_the_password_and_ends_the_old_session()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var signedIn = await SignUp(f, "asha@example.com");
        var oldCookie = CookiePair(await Login(f.Browser(), "asha@example.com", Password));
        var c = f.Browser();

        await c.PostAsJsonAsync("/api/auth/forgot-password", new { email = "asha@example.com" });
        var token = f.Emails.LastToken("asha@example.com", "reset");
        var weak = await c.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "short" });
        var done = await c.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = NewPassword });
        var again = await c.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "yet-another-long-passphrase" });

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.False(done.Headers.Contains("Set-Cookie")); // a reset signs nobody in
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/auth/me")).StatusCode);
        var raw = f.Raw();
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.Browser(), "asha@example.com", Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(f.Browser(), "asha@example.com", NewPassword)).StatusCode);
    }

    private static async Task<HttpResponseMessage> Login(HttpClient c, string email, string password, string? code = null) =>
        await c.PostAsJsonAsync("/api/auth/login", new { email, password, totpCode = code });

    [Fact]
    public async Task No_response_ever_contains_a_link_token()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com", verify: false);
        var token = f.Emails.LastToken("asha@example.com", "verify")!;
        var bodies = new List<string>
        {
            await (await c.PostAsync("/api/auth/resend-verification", null)).Content.ReadAsStringAsync(),
            await (await c.GetAsync("/api/auth/me")).Content.ReadAsStringAsync(),
            await (await c.PostAsJsonAsync("/api/auth/forgot-password", new { email = "asha@example.com" })).Content.ReadAsStringAsync(),
        };
        await Verify(f, c, "asha@example.com");
        bodies.Add(await c.GetStringAsync("/api/security/events"));
        bodies.Add(await c.GetStringAsync("/api/account"));

        var reset = f.Emails.LastToken("asha@example.com", "reset")!;
        foreach (var b in bodies)
        {
            Assert.DoesNotContain(token, b);
            Assert.DoesNotContain(reset, b);
        }
    }

    // ---- account endpoints ----

    [Fact]
    public async Task Account_endpoints_need_a_login()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = f.Browser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/account")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/account/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/account/delete", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsync("/api/account/mfa/setup", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/account/mfa/enable", new { code = "123456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/account/mfa/disable", new { password = Password })).StatusCode);
    }

    [Fact]
    public async Task Account_changes_need_the_app_header_even_when_signed_in()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com");
        c.DefaultRequestHeaders.Remove("X-Requested-With");

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/account/delete", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsync("/api/account/mfa/setup", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/account")).StatusCode);
    }

    [Fact]
    public async Task Deleting_over_http_needs_the_password_ends_the_session_and_leaves_other_users_alone()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var asha = await SignUp(f, "asha@example.com");
        var ravi = await SignUp(f, "ravi@example.com");
        await asha.PostAsJsonAsync("/api/sms/import", new { messages = new[] { "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26" } });
        await ravi.PostAsJsonAsync("/api/sms/import", new { messages = new[] { "Sent Rs.999.00 From HDFC Bank A/c *1234 To ZOMATO On 02/11/26" } });

        var wrong = await asha.PostAsJsonAsync("/api/account/delete", new { password = "wrong-password-1" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await asha.GetAsync("/api/transactions")).StatusCode);

        var ok = await asha.PostAsJsonAsync("/api/account/delete", new { password = Password });

        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await asha.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.Browser(), "asha@example.com", Password)).StatusCode);
        Assert.Contains("Zomato", await ravi.GetStringAsync("/api/transactions"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, (await Login(f.Browser(), "ravi@example.com", Password)).StatusCode);
    }

    [Fact]
    public async Task An_unverified_user_cannot_delete_or_export()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com", verify: false);

        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/account/delete", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/account/export")).StatusCode);
    }

    [Fact]
    public async Task The_export_downloads_as_a_file_with_the_users_own_data_only()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var asha = await SignUp(f, "asha@example.com");
        var ravi = await SignUp(f, "ravi@example.com");
        await asha.PostAsJsonAsync("/api/sms/import", new { messages = new[] { "Sent Rs.250.00 From HDFC Bank A/c *1234 To SWIGGY On 02/11/26" } });
        await ravi.PostAsJsonAsync("/api/sms/import", new { messages = new[] { "Sent Rs.999.00 From HDFC Bank A/c *1234 To ZOMATO On 02/11/26" } });

        var res = await asha.GetAsync("/api/account/export");
        var body = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("attachment", res.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("pennypath-export.json", res.Content.Headers.ContentDisposition.FileName);
        Assert.Contains("Swiggy", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Zomato", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ravi@example.com", body);
        foreach (var secret in new[] { "passwordHash", "AQAAAA", "protectedMfaSecret", "tokenHash", "rawHash" })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
    }

    // ---- two-factor login over http ----

    [Fact]
    public async Task Two_factor_end_to_end_enable_then_sign_in_with_a_code_then_use_a_recovery_code_then_turn_off()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com");

        var setup = await (await c.PostAsync("/api/account/mfa/setup", null)).Content.ReadFromJsonAsync<JsonElement>();
        var secret = setup.GetProperty("secret").GetString()!;
        Assert.StartsWith("otpauth://totp/", setup.GetProperty("otpAuthUri").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/account/mfa/enable", new { code = "000000" })).StatusCode);
        var enabled = await c.PostAsJsonAsync("/api/account/mfa/enable", new { code = CodeFor(secret) });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(10, codes.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/account/mfa/setup", null)).StatusCode); // already on

        // password alone: no session, just a request for the code
        var noCode = await Login(f.Browser(), "asha@example.com", Password);
        Assert.Equal(HttpStatusCode.OK, noCode.StatusCode);
        Assert.True((await noCode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaRequired").GetBoolean());
        Assert.False(noCode.Headers.Contains("Set-Cookie"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.Browser(), "asha@example.com", Password, "000000")).StatusCode);

        // the code used to enable cannot be reused; the next step's code is fine
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.Browser(), "asha@example.com", Password, CodeFor(secret))).StatusCode);
        var ok = await Login(f.Browser(), "asha@example.com", Password, CodeFor(secret, +1));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True(ok.Headers.Contains("Set-Cookie"));

        // a recovery code works once
        Assert.Equal(HttpStatusCode.OK, (await Login(f.Browser(), "asha@example.com", Password, codes[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(f.Browser(), "asha@example.com", Password, codes[0])).StatusCode);

        // turning it off needs the password and a code
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/account/mfa/disable", new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/account/mfa/disable", new { password = Password, code = codes[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Login(f.Browser(), "asha@example.com", Password)).StatusCode);
    }

    [Fact]
    public async Task Deleting_an_account_with_two_factor_asks_for_the_code()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com");
        var secret = (await (await c.PostAsync("/api/account/mfa/setup", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("secret").GetString()!;
        await c.PostAsJsonAsync("/api/account/mfa/enable", new { code = CodeFor(secret) });

        var without = await c.PostAsJsonAsync("/api/account/delete", new { password = Password });
        Assert.Equal(HttpStatusCode.Conflict, without.StatusCode);
        Assert.True((await without.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mfaRequired").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/account/delete", new { password = Password, code = CodeFor(secret, +1) })).StatusCode);
    }

    [Fact]
    public async Task The_new_events_show_in_the_users_own_activity_without_personal_data()
    {
        using var f = new DbApiFactory(requireVerifiedEmail: true);
        var c = await SignUp(f, "asha@example.com");
        await c.PostAsJsonAsync("/api/auth/forgot-password", new { email = "asha@example.com" });
        await c.GetAsync("/api/account/export");

        var events = await c.GetStringAsync("/api/security/events");

        Assert.Contains("EmailVerified", events);
        Assert.Contains("PasswordResetRequested", events);
        Assert.Contains("DataExported", events);
        Assert.DoesNotContain("asha@example.com", events);
    }
}
