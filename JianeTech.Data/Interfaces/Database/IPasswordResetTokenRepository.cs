using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IPasswordResetTokenRepository
    {
        DbSet<PasswordResetToken> GetPasswordResetTokens();
        void AddPasswordResetToken(PasswordResetToken token);
        void UpdatePasswordResetToken(PasswordResetToken token);
    }
}
