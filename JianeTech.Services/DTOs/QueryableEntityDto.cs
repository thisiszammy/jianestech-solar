namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// Pagination envelope for every paged read. <see cref="TotalRows"/> is the count of
    /// the <em>filtered</em> query taken before Skip/Take, so the UI can compute a page
    /// count that agrees with the rows it was handed.
    /// </summary>
    public class QueryableEntityDto<T>
    {
        public List<T> Items { get; set; } = [];

        public int TotalRows { get; set; }
    }
}
