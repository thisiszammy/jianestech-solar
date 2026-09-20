namespace JianeTech.Api.Authentication;

/// <summary>
/// The two cookies that carry a session. Both are HttpOnly, so the Blazor client never
/// holds a token in JavaScript-reachable memory and an XSS bug cannot exfiltrate one.
/// </summary>
/// <remarks>
/// <para>
/// <b>SameSite=None is deliberate.</b> JianeTech.Client is served from its own origin and
/// calls this API cross-origin, so the browser will only attach these cookies to its fetch
/// requests when the cookie is marked <c>SameSite=None</c> — which in turn requires
/// <c>Secure</c>, hence HTTPS on both ends. The matching client-side halves are the CORS
/// policy's <c>AllowCredentials()</c> in <c>Program.cs</c> and
/// <c>BrowserRequestCredentials.Include</c> on the client's HttpClient. Change any one of
/// those three and the cookies silently stop being sent.
/// </para>
/// </remarks>
public static class AuthenticationCookies
{
    /// <summary>Holds the signed JWT itself.</summary>
    public const string AccessTokenName = "jt_atk";

    /// <summary>Holds the raw refresh-grant id; the database stores only its hash.</summary>
    public const string RefreshTokenName = "jt_rtk";

    private const string CookiePath = "/";

    public static CookieOptions AccessOptions(DateTime expiresOn) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Expires = expiresOn,
        Path = CookiePath,
    };

    public static CookieOptions RefreshOptions(DateTime expiresOn) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Expires = expiresOn,
        Path = CookiePath,
    };

    /// <summary>
    /// Must match the attributes the cookie was written with apart from Expires — the
    /// browser matches a delete on name + path + domain, and a mismatch leaves the cookie
    /// in place.
    /// </summary>
    public static CookieOptions ClearOptions => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.None,
        Path = CookiePath,
    };
}
