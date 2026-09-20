namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// One entry of the activity filter's option list. Served from the server rather than
    /// restated in the client so the wording stays owned by <c>ActivityEnumExtensions</c>
    /// and cannot drift between the filter and the rows it filters.
    /// </summary>
    public class ActivityOptionDTO
    {
        public int Activity { get; set; }

        public string Label { get; set; } = string.Empty;

        /// <summary>success / failed / blocked — lets the filter show the dot the rows will carry.</summary>
        public string Status { get; set; } = string.Empty;
    }
}
