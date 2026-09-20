using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class PasswordResetTokenRepository : BaseRepository<DatabaseContext>, IPasswordResetTokenRepository
    {
        public PasswordResetTokenRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<PasswordResetToken> GetPasswordResetTokens()
            => GetDbSet<PasswordResetToken>();

        public void AddPasswordResetToken(PasswordResetToken token)
            => GetDbSet<PasswordResetToken>().Add(token);

        public void UpdatePasswordResetToken(PasswordResetToken token)
            => GetDbSet<PasswordResetToken>().Update(token);
    }
}
