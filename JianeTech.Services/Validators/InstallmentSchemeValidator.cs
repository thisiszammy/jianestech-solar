using System.Diagnostics.CodeAnalysis;
using JianeTech.Data.Entities;
using JianeTech.Services.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Validators
{
    /// <summary>
    /// All validation for the <c>InstallmentScheme</c> resource. Stateless: registered as a
    /// singleton and shared across every request, so nothing may be held in a field.
    /// </summary>
    public class InstallmentSchemeValidator
    {
        /// <summary>Mirrors dbo.InstallmentSchemes.Description, nvarchar(200).</summary>
        private const int MaxDescriptionLength = 200;

        /// <summary>
        /// Fifty years. Not a business rule so much as a floor under the arithmetic: the
        /// configuration page draws a row per month, and a term entered with a stray zero
        /// should be refused here rather than turned into a schedule nobody can read.
        /// </summary>
        private const int MaxMonthPeriods = 600;

        /// <summary>
        /// The column is decimal(18, 2), which would take far more than this. The limit is
        /// set where a figure stops being a solar installation and starts being a typo.
        /// </summary>
        private const decimal MaxAmount = 999_999_999.99m;

        public Task ValidateCreateInstallmentScheme(
            InstallmentScheme scheme,
            IQueryable<InstallmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            ValidateTerms(scheme);
            return RequireDescriptionIsFree(scheme.Description, null, existingSchemes, cancellationToken);
        }

        /// <summary>
        /// The edit form's rules. Same terms as creation, plus the one difference that
        /// matters: the name check must ignore the row being edited, or saving a scheme
        /// without touching its description would report it as a clash with itself.
        /// </summary>
        public async Task ValidateUpdateInstallmentScheme(
            [NotNull] InstallmentScheme? scheme,
            InstallmentScheme proposed,
            IQueryable<InstallmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            if (scheme is null)
                throw new InstallmentSchemeValidationException("That scheme no longer exists.");

            ValidateTerms(proposed);

            await RequireDescriptionIsFree(
                proposed.Description, scheme.SchemeId, existingSchemes, cancellationToken);
        }

        /// <summary>
        /// Guards the register's on/off switch.
        /// </summary>
        /// <remarks>
        /// The name check on the way back on is the load-bearing one. Names are only held
        /// unique among schemes actually on offer — a withdrawn scheme must not reserve its
        /// description forever — so the name it used to hold may since have been taken.
        /// Refusing here is what stops a reactivation from putting two schemes called
        /// "Standard 24-month" in front of the same customer.
        /// </remarks>
        public async Task ValidateSetInstallmentSchemeActive(
            [NotNull] InstallmentScheme? scheme,
            bool isActive,
            IQueryable<InstallmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            if (scheme is null)
                throw new InstallmentSchemeValidationException("That scheme no longer exists.");

            if (scheme.IsActive == isActive)
                throw new InstallmentSchemeValidationException(
                    $"'{scheme.Description}' is already {(isActive ? "on offer" : "withdrawn")}.");

            if (isActive)
            {
                await RequireDescriptionIsFree(
                    scheme.Description, scheme.SchemeId, existingSchemes, cancellationToken);
            }
        }

        /// <summary>
        /// The terms themselves, in the order a reader fills them in, so the first thing
        /// they are told about is the first thing they typed.
        /// </summary>
        private static void ValidateTerms(InstallmentScheme scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme.Description))
                throw new InstallmentSchemeValidationException("Description is required.");

            if (scheme.Description.Length > MaxDescriptionLength)
                throw new InstallmentSchemeValidationException(
                    $"Description must be {MaxDescriptionLength} characters or fewer.");

            RequireAmountInRange(scheme.PrincipalAmount, "Principal amount");
            RequireAmountInRange(scheme.DownPayment, "Down payment");
            RequireAmountInRange(scheme.PeriodicPayment, "Monthly payment");
            RequireAmountInRange(scheme.ReferenceBillAmount, "Reference bill amount");

            if (scheme.PrincipalAmount <= 0)
                throw new InstallmentSchemeValidationException("Principal amount must be more than zero.");

            if (scheme.PeriodicPayment <= 0)
                throw new InstallmentSchemeValidationException("Monthly payment must be more than zero.");

            // Strictly less, not at most: a down payment covering the whole price leaves
            // nothing to finance, and a scheme that finances nothing is not a scheme.
            if (scheme.DownPayment >= scheme.PrincipalAmount)
                throw new InstallmentSchemeValidationException(
                    "Down payment must be less than the principal amount — otherwise there is nothing left to pay off.");

            if (scheme.MonthPeriods < 1)
                throw new InstallmentSchemeValidationException("Term must be at least one month.");

            if (scheme.MonthPeriods > MaxMonthPeriods)
                throw new InstallmentSchemeValidationException(
                    $"Term must be {MaxMonthPeriods} months or fewer.");

            // The fund cannot pay a household to take an installation. A total of payments
            // below the balance financed is not a discount, it is a subsidy entered by
            // accident — usually a monthly figure short by a digit.
            var financed = scheme.PrincipalAmount - scheme.DownPayment;
            var totalOfPayments = scheme.PeriodicPayment * scheme.MonthPeriods;

            if (totalOfPayments < financed)
                throw new InstallmentSchemeValidationException(
                    $"{scheme.MonthPeriods} payments of {scheme.PeriodicPayment:N2} come to {totalOfPayments:N2}, "
                    + $"which is less than the {financed:N2} left to finance. Raise the monthly payment or the term.");
        }

        private static void RequireAmountInRange(decimal value, string field)
        {
            if (value < 0)
                throw new InstallmentSchemeValidationException($"{field} cannot be negative.");

            if (value > MaxAmount)
                throw new InstallmentSchemeValidationException(
                    $"{field} must be {MaxAmount:N2} or less.");
        }

        /// <summary>
        /// Uniqueness is checked against schemes on offer only, matching how the register
        /// reads: two live schemes with the same description would be indistinguishable in
        /// the row that names them, but a withdrawn one holding its name forever would mean
        /// a description could only ever be used once.
        /// </summary>
        private static async Task RequireDescriptionIsFree(
            string description,
            int? ignoreSchemeId,
            IQueryable<InstallmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            var trimmed = description?.Trim() ?? string.Empty;

            // Ids start at 1, so zero excludes nothing — which is exactly what a scheme
            // that does not exist yet needs.
            var ignore = ignoreSchemeId ?? 0;

            var taken = await existingSchemes.AnyAsync(
                s => s.IsActive && s.SchemeId != ignore && s.Description == trimmed,
                cancellationToken);

            if (taken)
                throw new InstallmentSchemeValidationException(
                    $"A scheme on offer is already called '{trimmed}'.");
        }
    }
}
