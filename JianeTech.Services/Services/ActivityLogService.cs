using JianeTech.Data.Enums;
using JianeTech.Data.Interfaces.Database;
using JianeTech.Services.DTOs;
using JianeTech.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// Paged reads over the audit trail. Read-only: it performs no writes, so it ships no
    /// validator and no service-scoped exception.
    /// </summary>
    public class ActivityLogService : IActivityLogService
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        private readonly IActivityLogRepository _activityLogRepository;

        public ActivityLogService(IActivityLogRepository activityLogRepository)
        {
            _activityLogRepository = activityLogRepository;
        }

        public async Task<QueryableEntityDto<ActivityLogBatchDTO>> GetActivityLogBatchDTOs(
            int page,
            int pageSize,
            string? search,
            int? activity,
            DateTime? from,
            DateTime? to,
            CancellationToken cancellationToken)
        {
            // Clamped rather than rejected: paging inputs arrive from a query string, and a
            // nonsense page size should degrade to a sane one, not 500 the feed.
            page = page < 1 ? 1 : page;
            pageSize = pageSize switch
            {
                < 1 => DefaultPageSize,
                > MaxPageSize => MaxPageSize,
                _ => pageSize,
            };

            var query = _activityLogRepository.GetActivityLogs().AsQueryable();

            if (activity.HasValue)
            {
                query = query.Where(l => l.Activity == activity.Value);
            }

            if (from.HasValue)
            {
                var fromDate = from.Value.Date;
                query = query.Where(l => l.ExecutedOn >= fromDate);
            }

            if (to.HasValue)
            {
                // Callers pass a calendar day; make the bound inclusive of that whole day.
                var toExclusive = to.Value.Date.AddDays(1);
                query = query.Where(l => l.ExecutedOn < toExclusive);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(l =>
                    l.Detail.Contains(term)
                    || l.IpAddress.Contains(term)
                    || (l.ExecutedByNavigation != null
                        && (l.ExecutedByNavigation.Username.Contains(term)
                            || l.ExecutedByNavigation.FirstName.Contains(term)
                            || l.ExecutedByNavigation.LastName.Contains(term)
                            || l.ExecutedByNavigation.Email.Contains(term))));
            }

            // The page is a page of batches. Skip/Take has to land on whole operations, so
            // the keys are selected first and the rows fetched for exactly those keys —
            // paging the rows instead would cut a three-step operation across the boundary
            // and show two of its steps under a heading claiming three.
            var batches = query
                .GroupBy(l => l.BatchId)
                .Select(g => new { BatchId = g.Key, LatestOn = g.Max(l => l.ExecutedOn) });

            // Counted off the filtered query before Skip/Take, so the page count the UI
            // derives agrees with what it is handed. This is a count of operations.
            var totalRows = await batches.CountAsync(cancellationToken);

            var pageBatchIds = await batches
                .OrderByDescending(b => b.LatestOn)
                // BatchId only breaks ties, and only so the order is stable: every row of a
                // batch carries the same timestamp, so without it two operations logged in
                // the same second could swap places between one page request and the next.
                .ThenByDescending(b => b.BatchId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(b => b.BatchId)
                .ToListAsync(cancellationToken);

            var rows = await query
                .Where(l => pageBatchIds.Contains(l.BatchId))
                .Select(l => new
                {
                    l.ActivityLogId,
                    l.BatchId,
                    l.ExecutionOrder,
                    l.Activity,
                    l.Detail,
                    l.IpAddress,
                    l.UserAgent,
                    l.ExecutedOn,
                    l.ExecutedBy,
                    l.ReferenceType,
                    l.Reference,
                    ExecutorFirstName = l.ExecutedByNavigation != null ? l.ExecutedByNavigation.FirstName : null,
                    ExecutorLastName = l.ExecutedByNavigation != null ? l.ExecutedByNavigation.LastName : null,
                    ExecutorEmail = l.ExecutedByNavigation != null ? l.ExecutedByNavigation.Email : null,
                })
                .ToListAsync(cancellationToken);

            // ToLabel()/ToStatus() are extension methods over a switch, so neither survives
            // translation to SQL — the query above stops at an anonymous type and the
            // display shape is assembled here.
            var steps = rows.Select(r =>
            {
                var activityEnum = (ActivityEnum)r.Activity;
                return new ActivityLogDTO
                {
                    ActivityLogId = r.ActivityLogId,
                    BatchId = r.BatchId,
                    ExecutionOrder = r.ExecutionOrder,
                    Activity = r.Activity,
                    ActivityLabel = activityEnum.ToLabel(),
                    Status = activityEnum.ToStatus(),
                    Detail = r.Detail,
                    IpAddress = r.IpAddress,
                    UserAgent = r.UserAgent,
                    ExecutedOn = r.ExecutedOn,
                    ExecutedBy = r.ExecutedBy,
                    ExecutedByName = DashboardService.ComposeName(r.ExecutorFirstName, r.ExecutorLastName),
                    ExecutedByEmail = r.ExecutorEmail,
                    ReferenceType = r.ReferenceType,
                    Reference = r.Reference,
                };
            }).ToList();

            // Grouped in memory rather than in SQL, and walked in the key order the page
            // query already established — re-sorting here would undo it.
            var byBatch = steps
                .GroupBy(s => s.BatchId)
                .ToDictionary(g => g.Key, g => g.OrderBy(s => s.ExecutionOrder).ToList());

            var items = pageBatchIds
                .Where(byBatch.ContainsKey)
                .Select(batchId =>
                {
                    var batchSteps = byBatch[batchId];

                    // Read off the first step. Every row of a batch carries the same actor,
                    // clock reading and client by construction: one service method call
                    // stamps all of them from the same three values.
                    var lead = batchSteps[0];

                    return new ActivityLogBatchDTO
                    {
                        BatchId = batchId,
                        ExecutedOn = lead.ExecutedOn,
                        ExecutedBy = lead.ExecutedBy,
                        ExecutedByName = lead.ExecutedByName,
                        ExecutedByEmail = lead.ExecutedByEmail,
                        IpAddress = lead.IpAddress,
                        UserAgent = lead.UserAgent,
                        Items = batchSteps,
                    };
                })
                .ToList();

            return new QueryableEntityDto<ActivityLogBatchDTO>
            {
                Items = items,
                TotalRows = totalRows,
            };
        }

        public List<ActivityOptionDTO> GetActivityOptionDTOs()
            => Enum.GetValues<ActivityEnum>()
                .Select(activity => new ActivityOptionDTO
                {
                    Activity = (int)activity,
                    Label = activity.ToLabel(),
                    Status = activity.ToStatus(),
                })
                .OrderBy(option => option.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }
}
