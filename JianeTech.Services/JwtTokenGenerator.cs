using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using JianeTech.Data.Entities;
using JianeTech.Data.Enums;
using JianeTech.Services.Interfaces;

namespace JianeTech.Services
{
    /// <summary>
    /// Mints the access tokens the auth cookie carries. Registered as a singleton: the
    /// issuer, audience and signing key are read once at startup.
    /// </summary>
    public class JwtTokenGenerator : IJwtTokenGenerator
    {
        /// <summary>
        /// Claim type holding the account's <see cref="UserTypeEnum"/> name. Exposed so
        /// authorization policies and <c>HttpContext</c> helpers reference the same string
        /// rather than re-declaring it.
        /// </summary>
        public const string RoleClaimType = ClaimTypes.Role;

        private readonly string _issuer;
        private readonly string _audience;
        private readonly SigningCredentials _signingCredentials;

        public JwtTokenGenerator(IConfiguration configuration)
        {
            _issuer = configuration["Jwt:Issuers"]
                ?? throw new InvalidOperationException("Jwt:Issuers is not configured.");

            _audience = configuration["Jwt:Audience"]
                ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

            var keyString = configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured.");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

        public string GenerateAccessToken(
            User user,
            DateTime issuedOn,
            DateTime expiresOn)
        {
            var claims = new List<Claim>
            {
                // Sub is what HttpContext.GetActingUserId() reads back as the acting user.
                new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(JwtRegisteredClaimNames.GivenName, user.FirstName ?? string.Empty),
                new(JwtRegisteredClaimNames.FamilyName, user.LastName ?? string.Empty),
                new(JwtRegisteredClaimNames.PreferredUsername, user.Username ?? string.Empty),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new(RoleClaimType, ((UserTypeEnum)user.UserType).ToString()),
            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                notBefore: issuedOn,
                expires: expiresOn,
                signingCredentials: _signingCredentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
