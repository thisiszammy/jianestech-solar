namespace JianeTech.Services.DTOs
{
    /// <summary>One row of the audit feed, already resolved for display.</summary>
    public class ActivityLogDTO
    {
        public Guid ActivityLogId { get; set; }

        public Guid BatchId { get; set; }

        public int ExecutionOrder { get; set; }

        public int Activity { get; set; }

        /// <summary>Short column label, e.g. "Sign in".</summary>
        public string ActivityLabel { get; set; } = string.Empty;

        /// <summary>Tone the row is painted with: success / failed / blocked.</summary>
        public string Status { get; set; } = string.Empty;

        public string Detail { get; set; } = string.Empty;

        public string IpAddress { get; set; } = string.Empty;

        public string UserAgent { get; set; } = string.Empty;

        public DateTime ExecutedOn { get; set; }

        public Guid? ExecutedBy { get; set; }

        /// <summary>Null when the action had no identifiable actor.</summary>
        public string? ExecutedByName { get; set; }

        public string? ExecutedByEmail { get; set; }

        public int? ReferenceType { get; set; }

        public Guid? Reference { get; set; }
    }
}
