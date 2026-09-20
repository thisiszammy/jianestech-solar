using JianeTech.Data.Enums;

namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// What a successful sign-in or rotation produces. <see cref="RefreshTokenId"/> is the
    /// <em>raw</em> Guid destined for the cookie — the database only ever stores its hash.
    /// </summary>
    public class AuthenticationDTO
    {
        public required string AccessToken { get; set; }

        public DateTime AccessTokenExpiresOn { get; set; }

        public Guid RefreshTokenId { get; set; }

        public DateTime RefreshTokenExpiresOn { get; set; }

        public TokenTypeEnum RefreshTokenType { get; set; }

        public required AuthenticatedUserDTO User { get; set; }
    }
}
