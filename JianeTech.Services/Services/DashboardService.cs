using JianeTech.Data.Enums;
using JianeTech.Data.Interfaces.Database;
using JianeTech.Services.DTOs;
using JianeTech.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// Assembles the back-office dashboard in one call. Read-only: it performs no writes,
    /// so it ships no validator and no service-scoped exception.
    /// </summary>
    public class DashboardService : IDashboardService
    {
        private const int RecentActivityCount = 8;
        private const int SignInActivityDays = 14;
        private const int RollingWindowDays = 30;

        private static readonly int SignedInActivity = (int)ActivityEnum.UserLoggedIn;
        private static readonly int SignInFailedActivity = (int)ActivityEnum.LoginFailed;
        private static readonly int AccountLockedActivity = (int)ActivityEnum.AccountLocked;

        private readonly IUserRepository _userRepository;
        private readonly IAccessRefreshTokenRepository _accessRefreshTokenRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly IDateTimeService _dateTime;

        public DashboardService(
            IUserRepository userRepository,
            IAccessRefreshTokenRepository accessRefreshTokenRepository,
            IActivityLogRepository activityLogRepository,
            IDateTimeService dateTime)
        {
            _userRepository = userRepository;
            _accessRefreshTokenRepository = accessRefreshTokenRepository;
            _activityLogRepository = activityLogRepository;
            _dateTime = dateTime;
        }

        public async Task<DashboardSnapshotDTO> GetDashboardSnapshotDTO(CancellationToken cancellationToken)
        {
            var now = _dateTime.Now();
            var rollingWindowStart = now.AddDays(-RollingWindowDays);

            // Inclusive of today, so the series always ends on the current day.
            var signInWindowStart = now.Date.AddDays(-(SignInActivityDays - 1));

            var users = _userRepository.GetUsers();
            var logs = _activityLogRepository.GetActivityLogs();
            var tokens = _accessRefreshTokenRepository.GetAccessRefreshTokens();

            var stats = new DashboardStatsDTO
            {
                ActiveAccounts = await users.CountAsync(
                    u => u.DeletedOn == null && u.IsActive && !u.IsLocked, cancellationToken),

                LockedAccounts = await users.CountAsync(
                    u => u.DeletedOn == null && u.IsLocked, cancellationToken),

                LiveSessions = await tokens.CountAsync(
                    t => t.UsedOn == null && t.InvalidatedOn == null && t.ExpiredOn > now, cancellationToken),

                SignIns30d = await logs.CountAsync(
                    l => l.Activity == SignedInActivity && l.ExecutedOn >= rollingWindowStart, cancellationToken),

                FailedSignIns30d = await logs.CountAsync(
                    l => (l.Activity == SignInFailedActivity || l.Activity == AccountLockedActivity)
                         && l.ExecutedOn >= rollingWindowStart, cancellationToken),

                TotalAuditedEvents = await logs.CountAsync(cancellationToken),
            };

            // Grouped in SQL, then densified in memory: the query returns only days that
            // have rows, and a chart needs every day in the window including the zeroes.
            var signInRows = await logs
                .Where(l => (l.Activity == SignedInActivity || l.Activity == SignInFailedActivity)
                            && l.ExecutedOn >= signInWindowStart)
                .GroupBy(l => new { Day = l.ExecutedOn.Date, l.Activity })
                .Select(g => new { g.Key.Day, g.Key.Activity, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var signInActivity = Enumerable.Range(0, SignInActivityDays)
                .Select(offset =>
                {
                    var day = signInWindowStart.AddDays(offset);
                    return new DashboardSignInBucketDTO
                    {
                        Day = day,
                        Success = signInRows
                            .Where(r => r.Day == day && r.Activity == SignedInActivity)
                            .Sum(r => r.Count),
                        Failed = signInRows
                            .Where(r => r.Day == day && r.Activity == SignInFailedActivity)
                            .Sum(r => r.Count),
                    };
                })
                .ToList();

            var statusBuckets = await users
                .GroupBy(u =>
                    u.DeletedOn != null ? "retired"
                    : u.IsLocked ? "locked"
                    : !u.IsActive ? "inactive"
                    : "active")
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            var accountStatus = new[]
            {
                ("Active",   "active",   "success"),
                ("Inactive", "inactive", "warn"),
                ("Locked",   "locked",   "danger"),
                ("Retired",  "retired",  "neutral"),
            }
            .Select(bucket => new DashboardAccountStatusDTO
            {
                Label = bucket.Item1,
                Tone = bucket.Item3,
                Count = statusBuckets.FirstOrDefault(b => b.Key == bucket.Item2)?.Count ?? 0,
            })
            .ToList();

            // Projected to an anonymous type first: ToLabel/ToStatus are C# switches EF
            // cannot translate, so the enum-to-wording step happens after materialization.
            var recentRows = await logs
                .OrderByDescending(l => l.ExecutedOn)
                .ThenByDescending(l => l.ExecutionOrder)
                .Take(RecentActivityCount)
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

            var recentActivity = recentRows.Select(r =>
            {
                var activity = (ActivityEnum)r.Activity;
                return new ActivityLogDTO
                {
                    ActivityLogId = r.ActivityLogId,
                    BatchId = r.BatchId,
                    ExecutionOrder = r.ExecutionOrder,
                    Activity = r.Activity,
                    ActivityLabel = activity.ToLabel(),
                    Status = activity.ToStatus(),
                    Detail = r.Detail,
                    IpAddress = r.IpAddress,
                    UserAgent = r.UserAgent,
                    ExecutedOn = r.ExecutedOn,
                    ExecutedBy = r.ExecutedBy,
                    ExecutedByName = ComposeName(r.ExecutorFirstName, r.ExecutorLastName),
                    ExecutedByEmail = r.ExecutorEmail,
                    ReferenceType = r.ReferenceType,
                    Reference = r.Reference,
                };
            }).ToList();

            return new DashboardSnapshotDTO
            {
                Stats = stats,
                SignInActivity = signInActivity,
                AccountStatus = accountStatus,
                RecentActivity = recentActivity,
                GeneratedOn = now,
            };
        }

        internal static string? ComposeName(string? firstName, string? lastName)
        {
            var composed = $"{firstName} {lastName}".Trim();
            return string.IsNullOrWhiteSpace(composed) ? null : composed;
        }
    }
}
