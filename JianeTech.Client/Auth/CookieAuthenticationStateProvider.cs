using System.Security.Claims;
using JianeTech.Client.Auth.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace JianeTech.Client.Auth;

/// <summary>
/// Authentication state for a client that cannot see its own token.
/// </summary>
/// <remarks>
/// The usual WASM pattern parses a JWT held in local storage. Here the token is in an
/// HttpOnly cookie by design, so this app has no way to inspect it. Instead the server is
/// asked — <c>GET /api/auth/me</c> either answers with the identity or 401s — and the
/// answer is cached for the session. That means the server stays the single authority on
/// whether someone is signed in; the client never decides for itself.
/// </remarks>
public class CookieAuthenticationStateProvider : AuthenticationStateProvider
{
    private const string AuthenticationType = "JianeTechCookie";

    private readonly FundApiClient _api;

    private AuthenticatedUser? _user;

    /// <summary>
    /// The in-flight (or completed) resolve. Caching the <em>Task</em> rather than
    /// its result is what makes concurrent callers share one request: on a cold
    /// page load the router and the page both ask for state before either has
    /// finished, and a bool flag would let both fire their own GET /api/auth/me.
    /// </summary>
    private Task<AuthenticationState>? _resolving;

    public CookieAuthenticationStateProvider(FundApiClient api)
    {
        _api = api;
    }

    /// <summary>The signed-in account, or null. Populated once state has been resolved.</summary>
    public AuthenticatedUser? CurrentUser => _user;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => _resolving ??= ResolveAsync();

    private async Task<AuthenticationState> ResolveAsync()
    {
        var response = await _api.MeAsync();
        _user = response.Code == 200 ? response.Data : null;
        return new AuthenticationState(BuildPrincipal(_user));
    }

    /// <summary>Adopts the identity a successful sign-in returned, without a second round trip.</summary>
    public void SignIn(AuthenticatedUser user)
    {
        _user = user;
        _resolving = Task.FromResult(new AuthenticationState(BuildPrincipal(_user)));
        NotifyAuthenticationStateChanged(_resolving);
    }

    /// <summary>
    /// Drops the cached identity. The cookies are cleared server-side by
    /// <c>POST /api/auth/logout</c>; this is only the client half.
    /// </summary>
    public void SignOut()
    {
        _user = null;
        _resolving = Task.FromResult(new AuthenticationState(BuildPrincipal(null)));
        NotifyAuthenticationStateChanged(_resolving);
    }

    /// <summary>Forces the next state read to ask the server again.</summary>
    public void Invalidate()
    {
        _resolving = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private static ClaimsPrincipal BuildPrincipal(AuthenticatedUser? user)
    {
        if (user is null)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email),

            // The UserTypeEnum member's name, which is what the access token carries and
            // what [Authorize(Roles = ...)] checks against on both sides. It used to be
            // UserTypeLabel — display wording, "Administrator" — which was harmless while
            // nothing read roles, and would have made every gate in this app disagree
            // with every gate in the API the moment one did. The sidebar still shows the
            // friendly name; it reads it off the DTO, not off a claim.
            new(ClaimTypes.Role, RoleName(user.UserType)),
        };

        // Passing an authenticationType is what makes IsAuthenticated true.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType));
    }

    /// <summary>
    /// Mirrors <c>UserTypeEnum</c>: 0 is SystemAdmin, anything else is a standard user.
    /// Restated rather than shared because this app does not reference the data project —
    /// keep it in step with the enum and with <see cref="ConsoleRoles"/>.
    /// </summary>
    private static string RoleName(int userType)
        => userType == 0 ? ConsoleRoles.Administrator : "User";
}
