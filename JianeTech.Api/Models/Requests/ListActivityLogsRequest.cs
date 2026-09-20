namespace JianeTech.Api.Models.Requests;

/// <summary>
/// Query-string filters for the audit feed. Every field is optional; the service clamps
/// out-of-range paging values rather than rejecting them.
/// </summary>
public class ListActivityLogsRequest
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    /// <summary>Matches detail text, IP, or the actor's name / username / email.</summary>
    public string? Search { get; set; }

    /// <summary>An <c>ActivityEnum</c> value to filter to.</summary>
    public int? Activity { get; set; }

    public DateTime? From { get; set; }

    public DateTime? To { get; set; }
}
