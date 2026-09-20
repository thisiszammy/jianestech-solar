using JianeTech.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Data.Interfaces.Database
{
    public interface IInvestmentSchemeRepository
    {
        DbSet<InvestmentScheme> GetInvestmentSchemes();
        void AddInvestmentScheme(InvestmentScheme investmentScheme);
        void UpdateInvestmentScheme(InvestmentScheme investmentScheme);
    }
}
