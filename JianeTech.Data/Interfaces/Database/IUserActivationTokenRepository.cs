using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IUserActivationTokenRepository
    {
        DbSet<UserActivationToken> GetUserActivationTokens();
        void AddUserActivationToken(UserActivationToken token);
        void UpdateUserActivationToken(UserActivationToken token);
    }
}
