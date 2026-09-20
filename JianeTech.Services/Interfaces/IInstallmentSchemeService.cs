using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    public interface IInstallmentSchemeService
    {
        /// <summary>
        /// One page of the scheme register, newest first. Every filter is optional;
        /// passing none returns every scheme paged.
        /// </summary>
        Task<QueryableEntityDto<InstallmentSchemeDTO>> GetInstallmentSchemeDTOs(
            int page,
            int pageSize,
            string? search,
            bool? isActive,
            int? minMonthPeriods,
            int? maxMonthPeriods,
            CancellationToken cancellationToken);

        /// <summary>
        /// One scheme, for the configuration page to open. Null when the id matches
        /// nothing — the caller answers 404 rather than an empty form.
        /// </summary>
        Task<InstallmentSchemeDTO?> GetInstallmentSchemeDTOById(
            int schemeId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Adds a scheme and returns its id. Throws
        /// <c>InstallmentSchemeValidationException</c>.
        /// </summary>
        Task<int> CreateInstallmentSchemeAsync(
            string description,
            decimal principalAmount,
            decimal downPayment,
            decimal periodicPayment,
            int monthPeriods,
            decimal referenceBillAmount,
            Guid? createdBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Rewrites a scheme's terms. Throws <c>InstallmentSchemeValidationException</c>.
        /// </summary>
        /// <remarks>
        /// The terms of a scheme already sold to a household are not changed by this:
        /// <c>dbo.Installments</c> copies the figures onto the agreement when it is
        /// written, so editing a scheme changes what is offered next, never what somebody
        /// already signed.
        /// </remarks>
        Task UpdateInstallmentSchemeAsync(
            int schemeId,
            string description,
            decimal principalAmount,
            decimal downPayment,
            decimal periodicPayment,
            int monthPeriods,
            decimal referenceBillAmount,
            Guid? updatedBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Puts a scheme on offer or withdraws it. Withdrawal is what the register calls
        /// delete: the row stays, its history stays, and the scheme simply stops being
        /// offered to anyone new. Throws <c>InstallmentSchemeValidationException</c>.
        /// </summary>
        Task SetInstallmentSchemeActiveAsync(
            int schemeId,
            bool isActive,
            Guid? updatedBy,
            CancellationToken cancellationToken);
    }
}
