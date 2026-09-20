using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IAccessRefreshTokenRepository
    {
        DbSet<AccessRefreshToken> GetAccessRefreshTokens();
        void AddAccessRefreshToken(AccessRefreshToken accessRefreshToken);
        void UpdateAccessRefreshToken(AccessRefreshToken accessRefreshToken);
    }
}
