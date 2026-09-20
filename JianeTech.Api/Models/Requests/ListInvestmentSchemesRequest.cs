using JianeTech.Data.Enums;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// Query-string filters for the investment scheme register. Every field is optional; the
/// service clamps out-of-range paging values rather than rejecting them.
/// </summary>
public class ListInvestmentSchemesRequest
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    /// <summary>Matches the scheme's description.</summary>
    public string? Search { get; set; }

    /// <summary>True for schemes still on offer, false for withdrawn ones.</summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Immediate or deferred — which end of the term the installation lands at. The one
    /// filter here that is not a range, because it is the one thing about a scheme that
    /// has exactly two answers.
    /// </summary>
    public SchemeTypeEnum? SchemeType { get; set; }

    /// <summary>
    /// Inclusive bounds on the term in years. Sent as a pair because the register's filter
    /// offers bands — up to two years, two to five — rather than an exact term.
    /// </summary>
    public int? MinInvestmentYears { get; set; }

    public int? MaxInvestmentYears { get; set; }
}
