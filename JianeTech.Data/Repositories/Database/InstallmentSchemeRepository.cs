using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class InstallmentSchemeRepository : BaseRepository<DatabaseContext>, IInstallmentSchemeRepository
    {
        public InstallmentSchemeRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<InstallmentScheme> GetInstallmentSchemes()
            => GetDbSet<InstallmentScheme>();

        public void AddInstallmentScheme(InstallmentScheme installmentScheme)
            => GetDbSet<InstallmentScheme>().Add(installmentScheme);

        public void UpdateInstallmentScheme(InstallmentScheme installmentScheme)
            => GetDbSet<InstallmentScheme>().Update(installmentScheme);
    }
}
