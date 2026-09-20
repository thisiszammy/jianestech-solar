using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    public interface IActivityLogService
    {
        /// <summary>
        /// One page of the audit feed, newest first, grouped into the operations that
        /// produced it. Every filter is optional; passing none returns the whole trail.
        ///
        /// A page is a page of <em>batches</em>, not of rows — paging rows would cut an
        /// operation in half at the page boundary and show half of it under a heading that
        /// claims the rest. <c>TotalRows</c> is therefore the number of matching batches.
        ///
        /// Filters apply per row, and the surviving rows are what get grouped: filtering to
        /// "Sign in failed" shows the failures, not the whole operation each belonged to.
        /// </summary>
        Task<QueryableEntityDto<ActivityLogBatchDTO>> GetActivityLogBatchDTOs(
            int page,
            int pageSize,
            string? search,
            int? activity,
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken);

        /// <summary>
        /// Every activity the trail can hold, worded for the console's event filter.
        /// Synchronous on purpose: this reads the enum, not the database, and wrapping it
        /// in a Task would only claim I/O that never happens.
        /// </summary>
        List<ActivityOptionDTO> GetActivityOptionDTOs();
    }
}
