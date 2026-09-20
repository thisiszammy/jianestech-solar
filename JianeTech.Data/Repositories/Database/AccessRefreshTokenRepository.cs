using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class AccessRefreshTokenRepository : BaseRepository<DatabaseContext>, IAccessRefreshTokenRepository
    {
        public AccessRefreshTokenRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<AccessRefreshToken> GetAccessRefreshTokens()
            => GetDbSet<AccessRefreshToken>();

        public void AddAccessRefreshToken(AccessRefreshToken accessRefreshToken)
            => GetDbSet<AccessRefreshToken>().Add(accessRefreshToken);

        public void UpdateAccessRefreshToken(AccessRefreshToken accessRefreshToken)
            => GetDbSet<AccessRefreshToken>().Update(accessRefreshToken);
    }
}
