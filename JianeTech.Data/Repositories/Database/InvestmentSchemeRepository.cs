using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Repositories.Database
{
    public class InvestmentSchemeRepository : BaseRepository<DatabaseContext>, IInvestmentSchemeRepository
    {
        public InvestmentSchemeRepository(IUnitOfWork unitOfWork) : base(unitOfWork) { }

        public DbSet<InvestmentScheme> GetInvestmentSchemes()
            => GetDbSet<InvestmentScheme>();

        public void AddInvestmentScheme(InvestmentScheme investmentScheme)
            => GetDbSet<InvestmentScheme>().Add(investmentScheme);

        public void UpdateInvestmentScheme(InvestmentScheme investmentScheme)
            => GetDbSet<InvestmentScheme>().Update(investmentScheme);
    }
}
