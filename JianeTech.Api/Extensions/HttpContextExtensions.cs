using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using JianeTech.Data.Enums;

namespace JianeTech.Api.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// The signed-in user behind the current request, or null when the caller is
    /// anonymous. Returns <c>Guid?</c> on purpose: auditable columns must persist as SQL
    /// NULL when the actor is unknown, because the all-zero Guid is unfilterable and
    /// quietly breaks every audit query that tries to exclude it.
    /// </summary>
    public static Guid? GetActingUserId(this HttpContext context)
    {
        // JwtSecurityTokenHandler maps "sub" onto NameIdentifier by default, but the raw
        // claim survives when that mapping is turned off - read either.
        var raw = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static bool IsInUserType(this HttpContext context, UserTypeEnum userType)
        => context.User.IsInRole(userType.ToString());
}
