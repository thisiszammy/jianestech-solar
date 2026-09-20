namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// One audited operation: every <c>ActivityLog</c> row that one service method call
    /// emitted, in the order it emitted them.
    ///
    /// The batch is the unit the schema was designed around — a service method mints one
    /// <c>BatchId</c> and pre-increments an <c>ExecutionOrder</c> per row — but nothing has
    /// ever read it back that way, so a lock-out arrived as two unrelated-looking lines and
    /// a token replay as six. Grouping is what turns them back into the operation they were.
    ///
    /// The fields here are the ones every row of a batch shares by construction: one call,
    /// one actor, one clock reading, one client. What differs per row — the activity and its
    /// detail — stays in <see cref="Items"/>.
    /// </summary>
    public class ActivityLogBatchDTO
    {
        public Guid BatchId { get; set; }

        public DateTime ExecutedOn { get; set; }

        public Guid? ExecutedBy { get; set; }

        /// <summary>Null when the operation had no identifiable actor.</summary>
        public string? ExecutedByName { get; set; }

        public string? ExecutedByEmail { get; set; }

        public string IpAddress { get; set; } = string.Empty;

        public string UserAgent { get; set; } = string.Empty;

        /// <summary>The batch's steps, in execution order.</summary>
        public List<ActivityLogDTO> Items { get; set; } = [];
    }
}
