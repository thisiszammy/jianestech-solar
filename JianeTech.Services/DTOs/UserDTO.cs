namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// One row of the account register, already resolved for display. Carries no
    /// credential material: <c>Password</c> and <c>Salt</c> never cross this boundary.
    /// </summary>
    public class UserDTO
    {
        public Guid UserId { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string PhoneNumber { get; set; } = string.Empty;

        public int UserType { get; set; }

        /// <summary>Role as the console words it, e.g. "Administrator".</summary>
        public string UserTypeLabel { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public bool IsLocked { get; set; }

        /// <summary>
        /// Invited and still waiting: at least one invitation was issued and none has been
        /// spent. An account created straight from a password — the seeded first
        /// administrator — is not pending, because it has nothing to activate.
        /// </summary>
        public bool IsPending { get; set; }

        public DateTime? LockedUntil { get; set; }

        /// <summary>active / pending / inactive / locked — one word the table both prints and paints with.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Same vocabulary the activity ledger's dots use: success / failed / blocked.</summary>
        public string StatusTone { get; set; } = string.Empty;

        public DateTime? LastSignInOn { get; set; }

        public DateTime? CreatedOn { get; set; }

        /// <summary>Null when the account was seeded with no identifiable actor.</summary>
        public string? CreatedByName { get; set; }
    }
}
