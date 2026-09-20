using System.Diagnostics.CodeAnalysis;
using JianeTech.Data.Entities;
using JianeTech.Data.Enums;
using JianeTech.Services.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Validators
{
    /// <summary>
    /// All validation for the <c>InvestmentScheme</c> resource. Stateless: registered as a
    /// singleton and shared across every request, so nothing may be held in a field.
    /// </summary>
    public class InvestmentSchemeValidator
    {
        /// <summary>Mirrors dbo.InvestmentSchemes.Description, nvarchar(200).</summary>
        private const int MaxDescriptionLength = 200;

        /// <summary>
        /// Fifty years, the same ceiling the installment side puts on a term. Not a
        /// business rule so much as a floor under the arithmetic: the configuration page
        /// draws a row per month, and a term entered with a stray zero should be refused
        /// here rather than turned into a schedule nobody can read.
        /// </summary>
        private const int MaxInvestmentYears = 50;

        /// <summary>
        /// dbo.InvestmentSchemes.IncomeInterest is decimal(5, 2), so this is the column's
        /// own ceiling rather than a judgement about what a fund would offer. The figure is
        /// a rate per annum. What catches a mistyped one is the configuration page, which
        /// prints what it accumulates to over the term and what the placement would come
        /// to, not a threshold picked out of the air in this file.
        /// </summary>
        private const decimal MaxIncomeInterest = 999.99m;

        /// <summary>
        /// The money columns are decimal(18, 2), which would take far more than this. The
        /// limit is set where a figure stops being a placement and starts being a typo.
        /// </summary>
        private const decimal MaxAmount = 999_999_999.99m;

        public Task ValidateCreateInvestmentScheme(
            InvestmentScheme scheme,
            IQueryable<InvestmentScheme> existingSchemes,
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
        public async Task ValidateUpdateInvestmentScheme(
            [NotNull] InvestmentScheme? scheme,
            InvestmentScheme proposed,
            IQueryable<InvestmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            if (scheme is null)
                throw new InvestmentSchemeValidationException("That scheme no longer exists.");

            ValidateTerms(proposed);

            await RequireDescriptionIsFree(
                proposed.Description, scheme.SchemeId, existingSchemes, cancellationToken);
        }

        /// <summary>
        /// Guards the register's on/off switch.
        /// </summary>
        /// <remarks>
        /// The name check on the way back on is the load-bearing one. Names are only held
        /// unique among schemes actually on offer — a withdrawn scheme must not reserve
        /// its description forever — so the name it used to hold may since have been
        /// taken. Refusing here is what stops a reactivation from putting two schemes
        /// called "Five-year deferred" in front of the same investor.
        /// </remarks>
        public async Task ValidateSetInvestmentSchemeActive(
            [NotNull] InvestmentScheme? scheme,
            bool isActive,
            IQueryable<InvestmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            if (scheme is null)
                throw new InvestmentSchemeValidationException("That scheme no longer exists.");

            if (scheme.IsActive == isActive)
                throw new InvestmentSchemeValidationException(
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
        private static void ValidateTerms(InvestmentScheme scheme)
        {
            if (string.IsNullOrWhiteSpace(scheme.Description))
                throw new InvestmentSchemeValidationException("Description is required.");

            if (scheme.Description.Length > MaxDescriptionLength)
                throw new InvestmentSchemeValidationException(
                    $"Description must be {MaxDescriptionLength} characters or fewer.");

            // The two structures are not interchangeable — they decide which end of the
            // term the installation lands at, and with it the whole shape of the payout
            // schedule — so an unrecognised value is refused rather than defaulted.
            if (!Enum.IsDefined(typeof(SchemeTypeEnum), scheme.SchemeType))
                throw new InvestmentSchemeValidationException(
                    "Choose whether the installation is delivered at the start or the end of the term.");

            RequireAmountInRange(scheme.MinimumInvestment, "Minimum investment");
            RequireAmountInRange(scheme.FreeInstallationAmt, "Installation amount");

            if (scheme.MinimumInvestment <= 0)
                throw new InvestmentSchemeValidationException("Minimum investment must be more than zero.");

            if (scheme.IncomeInterest <= 0)
                throw new InvestmentSchemeValidationException(
                    "Profit interest must be more than zero — an investor places capital to earn it.");

            if (scheme.IncomeInterest > MaxIncomeInterest)
                throw new InvestmentSchemeValidationException(
                    $"Profit interest must be {MaxIncomeInterest:N2}% or less.");

            if (scheme.InvestmentYears < 1)
                throw new InvestmentSchemeValidationException("Term must be at least one year.");

            if (scheme.InvestmentYears > MaxInvestmentYears)
                throw new InvestmentSchemeValidationException(
                    $"Term must be {MaxInvestmentYears} years or fewer.");

            if (scheme.GracePeriodMonths < 0)
                throw new InvestmentSchemeValidationException("Grace period cannot be negative.");

            // The rule that makes a scheme coherent at all, and the counterpart of the
            // installment side's "the payments must cover what is financed". The periodic
            // return is the profit spread across the months left after the grace period,
            // so a grace period that swallows the whole term leaves nothing to divide by.
            var termMonths = scheme.InvestmentYears * 12;

            if (scheme.GracePeriodMonths >= termMonths)
                throw new InvestmentSchemeValidationException(
                    $"A grace period of {scheme.GracePeriodMonths} months leaves nothing of a "
                    + $"{scheme.InvestmentYears}-year term ({termMonths} months) to pay out over. "
                    + "Shorten the grace period or lengthen the term.");
        }

        private static void RequireAmountInRange(decimal value, string field)
        {
            if (value < 0)
                throw new InvestmentSchemeValidationException($"{field} cannot be negative.");

            if (value > MaxAmount)
                throw new InvestmentSchemeValidationException(
                    $"{field} must be {MaxAmount:N2} or less.");
        }

        /// <summary>
        /// Uniqueness is checked against schemes on offer only, matching how the register
        /// reads: two live schemes with the same description would be indistinguishable in
        /// the row that names them, but a withdrawn one holding its name forever would
        /// mean a description could only ever be used once.
        /// </summary>
        private static async Task RequireDescriptionIsFree(
            string description,
            Guid? ignoreSchemeId,
            IQueryable<InvestmentScheme> existingSchemes,
            CancellationToken cancellationToken)
        {
            var trimmed = description?.Trim() ?? string.Empty;

            // The empty Guid is never a scheme's id, so it excludes nothing — which is
            // exactly what a scheme that does not exist yet needs.
            var ignore = ignoreSchemeId ?? Guid.Empty;

            var taken = await existingSchemes.AnyAsync(
                s => s.IsActive && s.SchemeId != ignore && s.Description == trimmed,
                cancellationToken);

            if (taken)
                throw new InvestmentSchemeValidationException(
                    $"A scheme on offer is already called '{trimmed}'.");
        }
    }
}
