namespace PennyPath.Api.Security;

/// <summary>
/// The login token lives only in this cookie: HttpOnly (scripts cannot read it), Secure, SameSite=Strict (never sent
/// on cross-site requests). With Secure on, the "__Host-" prefix makes browsers insist on Path=/ and no Domain.
/// </summary>
public sealed class SessionCookieSettings(bool secure)
{
    public bool Secure { get; } = secure;
    public string Name => Secure ? "__Host-pp_session" : "pp_session";

    public CookieOptions Options(DateTimeOffset? expires = null) => new()
    {
        HttpOnly = true,
        Secure = Secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };
}
