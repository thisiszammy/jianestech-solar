namespace JianeTech.Api.Models.Requests;

/// <summary>
/// Query-string filters for the scheme register. Every field is optional; the service
/// clamps out-of-range paging values rather than rejecting them.
/// </summary>
public class ListInstallmentSchemesRequest
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    /// <summary>Matches the scheme's description.</summary>
    public string? Search { get; set; }

    /// <summary>True for schemes still on offer, false for withdrawn ones.</summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Inclusive bounds on the term in months. Sent as a pair because the register's
    /// filter offers bands — up to a year, one to two years — rather than an exact term.
    /// </summary>
    public int? MinMonthPeriods { get; set; }

    public int? MaxMonthPeriods { get; set; }
}
