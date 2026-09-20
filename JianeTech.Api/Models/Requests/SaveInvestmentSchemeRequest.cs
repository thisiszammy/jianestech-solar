using System.ComponentModel.DataAnnotations;
using JianeTech.Data.Enums;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The investment scheme configuration form as it arrives from the console — the same six
/// terms whether the scheme is being created or rewritten, because the form is the same
/// form.
/// </summary>
/// <remarks>
/// There is no acting-user id and no <c>IsActive</c>. Who saved the scheme is read from
/// the bearer token, and whether it is on offer is a separate act with its own audit line
/// — see <c>SetInvestmentSchemeActiveRequest</c>. Nothing derived comes over this wire
/// either: the periodic return, the per-annum profit and the payout schedule the console
/// draws are all computed from these six figures and are not facts the server is asked to
/// take on trust.
/// </remarks>
public class SaveInvestmentSchemeRequest
{
    [Required(ErrorMessage = "Description is required.")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Which end of the term the investor takes delivery of the installation.</summary>
    public SchemeTypeEnum SchemeType { get; set; }

    /// <summary>The floor under a placement on these terms.</summary>
    public decimal MinimumInvestment { get; set; }

    /// <summary>
    /// The profit interest per annum, as a percentage — 12.5 means 12.5% a year, not
    /// 0.125 and not 12.5% spread over the whole term. The column is decimal(5, 2) and
    /// could not hold a fraction to any useful precision.
    /// </summary>
    public decimal IncomeInterest { get; set; }

    /// <summary>Months at the head of the term that pay nothing.</summary>
    public int GracePeriodMonths { get; set; }

    /// <summary>The whole term, in years.</summary>
    public int InvestmentYears { get; set; }

    /// <summary>What the installation the investor receives at no cost is worth.</summary>
    public decimal FreeInstallationAmt { get; set; }
}
