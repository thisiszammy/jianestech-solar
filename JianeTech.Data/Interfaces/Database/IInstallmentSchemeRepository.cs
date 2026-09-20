using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IInstallmentSchemeRepository
    {
        DbSet<InstallmentScheme> GetInstallmentSchemes();
        void AddInstallmentScheme(InstallmentScheme installmentScheme);
        void UpdateInstallmentScheme(InstallmentScheme installmentScheme);
    }
}
