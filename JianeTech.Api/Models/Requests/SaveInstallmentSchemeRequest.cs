using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The scheme configuration form as it arrives from the console — the same five terms
/// whether the scheme is being created or rewritten, because the form is the same form.
/// </summary>
/// <remarks>
/// There is no acting-user id and no <c>IsActive</c>. Who saved the scheme is read from
/// the bearer token, and whether it is on offer is a separate act with its own audit line
/// — see <c>SetInstallmentSchemeActiveRequest</c>. Nothing derived comes over this wire
/// either: the rate and the schedule the console draws are computed from these five
/// figures and are not facts the server is asked to take on trust.
/// </remarks>
public class SaveInstallmentSchemeRequest
{
    [Required(ErrorMessage = "Description is required.")]
    public string Description { get; set; } = string.Empty;

    /// <summary>The cash price of the installation.</summary>
    public decimal PrincipalAmount { get; set; }

    /// <summary>Paid up front; the rest is what the scheme finances.</summary>
    public decimal DownPayment { get; set; }

    /// <summary>Due each month for <see cref="MonthPeriods"/> months.</summary>
    public decimal PeriodicPayment { get; set; }

    public int MonthPeriods { get; set; }

    /// <summary>The household's current monthly electricity bill.</summary>
    public decimal ReferenceBillAmount { get; set; }
}
