using JianeTech.Data.Enums;

namespace JianeTech.Api.Authentication;

/// <summary>
/// The role names <c>[Authorize(Roles = ...)]</c> checks against.
/// </summary>
/// <remarks>
/// <para>
/// The value has to be exactly what <c>JwtTokenGenerator</c> writes into the token, which
/// is the <see cref="UserTypeEnum"/> member's own name — not its display label. The two
/// are different strings ("SystemAdmin" against "Administrator"), and a policy written
/// against the label would compile, run, and quietly refuse every administrator in the
/// fund. <c>nameof</c> is what keeps them the same string: renaming the enum member moves
/// this with it.
/// </para>
/// <para>
/// Until public registration existed, every account in the database had been created by
/// an administrator, so <c>[Authorize]</c> alone was the whole of the access control and
/// nothing needed this. <c>POST /api/account/register</c> is what changed that: anybody
/// can now hold an account, so every endpoint that runs the fund rather than merely
/// belonging to one has to say so.
/// </para>
/// </remarks>
public static class ConsoleRoles
{
    /// <summary>Full access, including the account register and the audit trail.</summary>
    public const string Administrator = nameof(UserTypeEnum.SystemAdmin);
}
