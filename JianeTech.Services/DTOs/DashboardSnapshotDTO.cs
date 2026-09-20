namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// Everything the back-office dashboard renders, assembled in one service call so the
    /// page makes a single request and every tile reflects the same instant.
    /// </summary>
    public class DashboardSnapshotDTO
    {
        public required DashboardStatsDTO Stats { get; set; }

        /// <summary>One bucket per day, oldest first, with no gaps — days with no activity are zeroes.</summary>
        public List<DashboardSignInBucketDTO> SignInActivity { get; set; } = [];

        public List<DashboardAccountStatusDTO> AccountStatus { get; set; } = [];

        public List<ActivityLogDTO> RecentActivity { get; set; } = [];

        public DateTime GeneratedOn { get; set; }
    }

    public class DashboardStatsDTO
    {
        public int ActiveAccounts { get; set; }

        public int LockedAccounts { get; set; }

        /// <summary>Refresh grants that are neither used nor revoked and have not expired.</summary>
        public int LiveSessions { get; set; }

        public int SignIns30d { get; set; }

        public int FailedSignIns30d { get; set; }

        /// <summary>Total audited writes on record — the size of the trail itself.</summary>
        public int TotalAuditedEvents { get; set; }
    }

    public class DashboardSignInBucketDTO
    {
        public DateTime Day { get; set; }

        public int Success { get; set; }

        public int Failed { get; set; }
    }

    public class DashboardAccountStatusDTO
    {
        public string Label { get; set; } = string.Empty;

        /// <summary>Palette key the UI maps to a colour: success / warn / danger / neutral.</summary>
        public string Tone { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
