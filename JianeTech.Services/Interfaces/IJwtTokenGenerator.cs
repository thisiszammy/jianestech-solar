using JianeTech.Data.Entities;

namespace JianeTech.Services.Interfaces
{
    public interface IJwtTokenGenerator
    {
        string GenerateAccessToken(
            User user,
            DateTime issuedOn,
            DateTime expiresOn);
    }
}
