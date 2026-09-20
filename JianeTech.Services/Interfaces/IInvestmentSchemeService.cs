using JianeTech.Data.Enums;
using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    public interface IInvestmentSchemeService
    {
        /// <summary>
        /// One page of the investment scheme register, newest first. Every filter is
        /// optional; passing none returns every scheme paged.
        /// </summary>
        Task<QueryableEntityDto<InvestmentSchemeDTO>> GetInvestmentSchemeDTOs(
            int page,
            int pageSize,
            string? search,
            bool? isActive,
            SchemeTypeEnum? schemeType,
            int? minInvestmentYears,
            int? maxInvestmentYears,
            CancellationToken cancellationToken);

        /// <summary>
        /// One scheme, for the configuration page to open. Null when the id matches
        /// nothing — the caller answers 404 rather than an empty form.
        /// </summary>
        Task<InvestmentSchemeDTO?> GetInvestmentSchemeDTOById(
            Guid schemeId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Adds a scheme and returns its id. Throws
        /// <c>InvestmentSchemeValidationException</c>.
        /// </summary>
        Task<Guid> CreateInvestmentSchemeAsync(
            string description,
            SchemeTypeEnum schemeType,
            decimal minimumInvestment,
            decimal incomeInterest,
            int gracePeriodMonths,
            int investmentYears,
            decimal freeInstallationAmt,
            Guid? createdBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Rewrites a scheme's terms. Throws <c>InvestmentSchemeValidationException</c>.
        /// </summary>
        /// <remarks>
        /// Capital already placed on a scheme is not repriced by this: <c>dbo.Investments</c>
        /// carries the figures an investor was signed on, so editing a scheme changes what
        /// is offered next, never what somebody already holds.
        /// </remarks>
        Task UpdateInvestmentSchemeAsync(
            Guid schemeId,
            string description,
            SchemeTypeEnum schemeType,
            decimal minimumInvestment,
            decimal incomeInterest,
            int gracePeriodMonths,
            int investmentYears,
            decimal freeInstallationAmt,
            Guid? updatedBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Puts a scheme on offer or withdraws it. Withdrawal is what the register calls
        /// delete: the row stays, its history stays, and the scheme simply stops being
        /// offered to anyone new. Throws <c>InvestmentSchemeValidationException</c>.
        /// </summary>
        Task SetInvestmentSchemeActiveAsync(
            Guid schemeId,
            bool isActive,
            Guid? updatedBy,
            CancellationToken cancellationToken);
    }
}
