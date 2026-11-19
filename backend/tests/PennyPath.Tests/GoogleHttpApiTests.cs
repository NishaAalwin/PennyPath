using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PennyPath.Application.Gmail;
using PennyPath.Application.Parsing;
using PennyPath.Infrastructure.Gmail;

namespace PennyPath.Tests;

public class GoogleHttpApiTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request, body));
            var (status, json) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private static GoogleOptions Opts(string redirect = "https://localhost:8443/api/gmail/callback") =>
        new() { ClientId = "client-id.apps.googleusercontent.com", ClientSecret = "client-secret-xyz", RedirectUri = redirect };

    private static (GoogleHttpApi Api, StubHandler Stub) Create(Func<HttpRequestMessage, string?, (HttpStatusCode, string)> respond, GoogleOptions? o = null)
    {
        var stub = new StubHandler(respond);
        return (new GoogleHttpApi(new HttpClient(stub), Options.Create(o ?? Opts())), stub);
    }

    private static string Single(string mime, string content) =>
        "{\"id\":\"m1\",\"internalDate\":\"1793556000000\",\"payload\":{\"mimeType\":\"" + mime + "\",\"headers\":[],\"body\":{\"data\":\"" + B64Url(content) + "\"}}}";

    private static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void It_is_configured_only_with_an_id_a_secret_and_an_https_redirect()
    {
        Assert.True(Create((_, _) => (HttpStatusCode.OK, "{}")).Api.IsConfigured);
        Assert.False(Create((_, _) => (HttpStatusCode.OK, "{}"), new GoogleOptions()).Api.IsConfigured);
        Assert.False(Create((_, _) => (HttpStatusCode.OK, "{}"), Opts("http://localhost:8443/cb")).Api.IsConfigured);
        Assert.False(Create((_, _) => (HttpStatusCode.OK, "{}"), new GoogleOptions { ClientId = "x", RedirectUri = "https://a/cb" }).Api.IsConfigured);
    }

    [Fact]
    public void The_consent_address_asks_for_read_only_access_with_pkce_and_never_contains_the_secret()
    {
        var url = Create((_, _) => (HttpStatusCode.OK, "{}")).Api.BuildAuthorizationUrl("state-123", "challenge-456");

        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        var q = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal("https://www.googleapis.com/auth/gmail.readonly", q["scope"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("challenge-456", q["code_challenge"]);
        Assert.Equal("state-123", q["state"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Equal("https://localhost:8443/api/gmail/callback", q["redirect_uri"]);
        Assert.DoesNotContain("client-secret-xyz", url);
        Assert.DoesNotContain("secret", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_code_exchange_sends_the_secret_in_the_post_body_not_the_address()
    {
        var (api, stub) = Create((_, _) => (HttpStatusCode.OK, """{"access_token":"a","refresh_token":"r1","scope":"https://www.googleapis.com/auth/gmail.readonly"}"""));

        var t = await api.ExchangeCodeAsync("the-code", "the-verifier", CancellationToken.None);

        Assert.Equal("r1", t.RefreshToken);
        Assert.Equal(GmailQuery.ReadOnlyScope, t.Scope);
        var (req, body) = stub.Calls.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://oauth2.googleapis.com/token", req.RequestUri!.ToString());
        Assert.Contains("client_secret=client-secret-xyz", body);
        Assert.Contains("code_verifier=the-verifier", body);
        Assert.Contains("grant_type=authorization_code", body);
    }

    [Fact]
    public async Task A_refresh_token_google_rejects_is_reported_as_an_auth_failure()
    {
        var (api, _) = Create((_, _) => (HttpStatusCode.BadRequest, """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}"""));

        await Assert.ThrowsAsync<GoogleAuthException>(() => api.RefreshAccessTokenAsync("r", CancellationToken.None));
    }

    [Fact]
    public async Task Other_token_endpoint_errors_are_network_errors_not_auth_failures()
    {
        var (api, _) = Create((_, _) => (HttpStatusCode.InternalServerError, "{}"));

        await Assert.ThrowsAsync<HttpRequestException>(() => api.RefreshAccessTokenAsync("r", CancellationToken.None));
    }

    [Fact]
    public async Task Gmail_calls_carry_the_access_token_in_the_header_only()
    {
        var (api, stub) = Create((_, _) => (HttpStatusCode.OK, """{"messages":[{"id":"a"},{"id":"b"}]}"""));

        var (ids, more) = await api.SearchMessageIdsAsync("access-tok-1", "after:1 (from:hdfcbank.net)", 300, CancellationToken.None);

        Assert.Equal(["a", "b"], ids);
        Assert.False(more);
        var (req, _) = stub.Calls.Single();
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
        Assert.Equal("access-tok-1", req.Headers.Authorization.Parameter);
        Assert.DoesNotContain("access-tok-1", req.RequestUri!.ToString());
        Assert.Contains("q=after%3A1", req.RequestUri.ToString());
    }

    [Fact]
    public async Task Search_stops_at_the_cap_and_says_there_is_more()
    {
        var calls = 0;
        var (api, _) = Create((_, _) =>
        {
            calls++;
            var ids = Enumerable.Range(0, 3).Select(i => $$"""{"id":"p{{calls}}-{{i}}"}""");
            return (HttpStatusCode.OK, $$"""{"messages":[{{string.Join(",", ids)}}],"nextPageToken":"next"}""");
        });

        var (ids, more) = await api.SearchMessageIdsAsync("t", "q", 5, CancellationToken.None);

        Assert.Equal(5, ids.Count);
        Assert.True(more);
    }

    [Fact]
    public async Task An_expired_access_token_is_an_auth_failure()
    {
        var (api, _) = Create((_, _) => (HttpStatusCode.Unauthorized, "{}"));

        await Assert.ThrowsAsync<GoogleAuthException>(() => api.SearchMessageIdsAsync("t", "q", 5, CancellationToken.None));
    }

    [Fact]
    public async Task A_message_is_decoded_from_its_plain_text_part_with_subject_and_ist_date()
    {
        var text = "Rs 450 spent on card XX9876 at AMAZON on 02-Nov-26";
        var json = "{\"id\":\"m1\",\"internalDate\":\"1793556000000\",\"payload\":{\"mimeType\":\"multipart/alternative\","
            + "\"headers\":[{\"name\":\"From\",\"value\":\"alerts@hdfcbank.net\"},{\"name\":\"Subject\",\"value\":\"Card spend alert\"},{\"name\":\"Authentication-Results\",\"value\":\"mx.google.com; dkim=pass header.i=@hdfcbank.net\"}],"
            + "\"parts\":[{\"mimeType\":\"text/plain\",\"body\":{\"data\":\"" + B64Url(text) + "\"}},"
            + "{\"mimeType\":\"text/html\",\"body\":{\"data\":\"" + B64Url("<p>ignored html</p>") + "\"}}]}}";
        var (api, stub) = Create((_, _) => (HttpStatusCode.OK, json));

        var m = (await api.GetMessageAsync("t", "m1", CancellationToken.None))!;

        Assert.Equal("Card spend alert", m.Subject);
        Assert.Equal("alerts@hdfcbank.net", m.From);
        Assert.Equal(["mx.google.com; dkim=pass header.i=@hdfcbank.net"], m.AuthenticationResults);
        Assert.Equal(text, m.Body.Trim());
        // 1793556000000 ms = 2026-11-01 18:00 UTC = 2026-11-01 23:30 IST
        Assert.Equal(new DateOnly(2026, 11, 1), m.ReceivedOn);
        Assert.Contains("format=full", stub.Calls.Single().Request.RequestUri!.Query);
    }

    [Fact]
    public async Task An_html_only_message_is_reduced_to_text()
    {
        var html = "<html><style>p{color:red}</style><body><p>INR&nbsp;250.00 debited</p><p>to <b>SWIGGY</b></p></body></html>";
        var json = Single("text/html", html);
        var (api, _) = Create((_, _) => (HttpStatusCode.OK, json));

        var m = (await api.GetMessageAsync("t", "m1", CancellationToken.None))!;

        Assert.DoesNotContain("color:red", m.Body);
        Assert.DoesNotContain("<", m.Body);
        Assert.Contains("250.00 debited", m.Body);
        Assert.Contains("SWIGGY", m.Body);
    }

    [Fact]
    public async Task A_message_deleted_after_listing_is_skipped()
    {
        var (api, _) = Create((_, _) => (HttpStatusCode.NotFound, "{}"));

        Assert.Null(await api.GetMessageAsync("t", "gone", CancellationToken.None));
    }

    [Fact]
    public async Task A_very_long_body_is_cut()
    {
        var json = Single("text/plain", new string('x', 100_000));
        var (api, _) = Create((_, _) => (HttpStatusCode.OK, json));

        var m = (await api.GetMessageAsync("t", "m1", CancellationToken.None))!;

        Assert.True(m.Body.Length <= 20_000);
    }

    [Fact]
    public async Task Revoking_sends_the_token_in_the_body_and_treats_already_invalid_as_done()
    {
        var (api, stub) = Create((_, _) => (HttpStatusCode.BadRequest, """{"error":"invalid_token"}"""));

        await api.RevokeAsync("tok-to-revoke", CancellationToken.None);

        var (req, body) = stub.Calls.Single();
        Assert.Equal("https://oauth2.googleapis.com/revoke", req.RequestUri!.ToString());
        Assert.Equal("token=tok-to-revoke", body);
    }
}

public class EmailTextTests
{
    private static readonly SmsTransactionParser Parser = new();
    private static readonly DateOnly Today = new(2026, 11, 4);

    [Fact]
    public void A_never_share_your_otp_footer_does_not_make_the_parser_discard_a_real_alert()
    {
        var body = "Dear Customer, Rs. 250.00 has been debited from account XX1234 to VPA swiggy@icici SWIGGY on 02-11-26. "
                 + "Your UPI transaction reference number is 123456789012. If you did not authorize this transaction, please report it immediately. "
                 + "Never share your OTP, PIN or password with anyone.";

        var p = Parser.Parse(EmailTextExtractor.BuildParsableText("You have done a UPI txn", body), Today);

        Assert.NotNull(p);
        Assert.Equal(250m, p.Amount);
        Assert.Equal(PennyPath.Domain.Entities.TransactionType.Expense, p.Type);
        Assert.Equal(new DateOnly(2026, 11, 2), p.Date);
    }

    [Fact]
    public void The_same_alert_is_discarded_without_the_cut_which_is_why_the_cut_exists()
    {
        var body = "Rs. 250.00 has been debited from account XX1234 to SWIGGY on 02-11-26. Never share your OTP with anyone.";

        Assert.Null(Parser.Parse(body, Today));
        Assert.NotNull(Parser.Parse(EmailTextExtractor.BuildParsableText("", body), Today));
    }

    [Fact]
    public void A_real_otp_message_is_still_ignored()
    {
        Assert.Null(Parser.Parse(EmailTextExtractor.BuildParsableText("OTP", "123456 is your OTP for the transaction of Rs. 250.00. Do not share it."), Today));
    }

    [Fact]
    public void Text_is_kept_short()
    {
        Assert.True(EmailTextExtractor.BuildParsableText("s", new string('a', 10_000)).Length <= EmailTextExtractor.MaxLength);
    }

    [Fact]
    public void Html_becomes_plain_text_without_tags_styles_or_entities()
    {
        var text = EmailTextExtractor.ToPlainText("<style>.a{}</style><script>alert(1)</script><table><tr><td>Rs.&nbsp;10</td><td>A &amp; B</td></tr></table>");

        Assert.DoesNotContain("alert", text);
        Assert.DoesNotContain("<", text);
        Assert.Contains("A & B", text);
    }

    [Fact]
    public void The_gmail_search_names_bank_senders_and_a_start_time()
    {
        var q = GmailQuery.Build(null, new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc));

        Assert.Contains("from:hdfcbank.net", q);
        Assert.Contains("(debited OR credited", q);
        Assert.DoesNotContain("in:inbox", q);
        Assert.Matches(@"^after:\d{10} ", q);
    }

    [Fact]
    public void A_last_sync_older_than_90_days_falls_back_to_90_days()
    {
        var now = new DateTime(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

        var old = GmailQuery.Build(now.AddDays(-200), now);

        Assert.Equal(GmailQuery.Build(null, now), old);
    }
}
