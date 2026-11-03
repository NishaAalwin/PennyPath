namespace PennyPath.Api.Security;

/// <summary>
/// Blocks cross-site request forgery for the cookie login. A state-changing request that relies on the cookie (or
/// signs in) must carry the custom header X-Requested-With: PennyPath, which a cross-site page cannot add without
/// a CORS preflight (this API allows no other origin), and must not be marked cross-site by the browser
/// (Sec-Fetch-Site). SameSite=Strict on the cookie is the first line of defence; this is the second.
/// </summary>
public sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Requested-With";
    public const string HeaderValue = "PennyPath";
    public const string CookieAuthItem = "pp.auth-from-cookie";

    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "TRACE" };
    private static readonly string[] AnonymousStateChanging = ["/api/auth/login", "/api/auth/register"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!SafeMethods.Contains(ctx.Request.Method) && NeedsCheck(ctx) && !IsSameOriginAjax(ctx.Request))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { error = "Request blocked: it did not come from the PennyPath app." });
            return;
        }

        await next(ctx);
    }

    private static bool NeedsCheck(HttpContext ctx) =>
        ctx.Items.ContainsKey(CookieAuthItem)
        || AnonymousStateChanging.Any(p => ctx.Request.Path.Equals(p, StringComparison.OrdinalIgnoreCase));

    private static bool IsSameOriginAjax(HttpRequest r)
    {
        var site = r.Headers["Sec-Fetch-Site"].ToString();
        return r.Headers[HeaderName] == HeaderValue && (site is "" or "same-origin" or "none");
    }
}
