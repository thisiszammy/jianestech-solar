using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class UserActivationTokenRepository : BaseRepository<DatabaseContext>, IUserActivationTokenRepository
    {
        public UserActivationTokenRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<UserActivationToken> GetUserActivationTokens()
            => GetDbSet<UserActivationToken>();

        public void AddUserActivationToken(UserActivationToken token)
            => GetDbSet<UserActivationToken>().Add(token);

        public void UpdateUserActivationToken(UserActivationToken token)
            => GetDbSet<UserActivationToken>().Update(token);
    }
}
