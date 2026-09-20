namespace JianeTech.Client.Auth;

/// <summary>
/// The role names this app's <c>[Authorize(Roles = ...)]</c> attributes check against.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>JianeTech.Api.Authentication.ConsoleRoles</c>, and must stay identical to
/// it: the value is the <c>UserTypeEnum</c> member's name, which is what the access token
/// carries, and it is <see cref="CookieAuthenticationStateProvider"/> that puts the same
/// string into this app's principal. Restated rather than shared because the client does
/// not reference the data project — keep the three in step.
/// </para>
/// <para>
/// A gate here is a courtesy, not a defence. Nothing in a WebAssembly app can be trusted
/// to enforce anything — the whole assembly is in the reader's browser — so these
/// attributes exist to keep somebody out of a page that would only 403 at them anyway.
/// The API's copy is the one that decides.
/// </para>
/// </remarks>
public static class ConsoleRoles
{
    /// <summary>Full access, including the account register and the audit trail.</summary>
    public const string Administrator = "SystemAdmin";
}
