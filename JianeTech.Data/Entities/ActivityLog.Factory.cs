using JianeTech.Data.Enums;

namespace JianeTech.Data.Entities
{
    public partial class ActivityLog
    {
        private const string UnknownClient = "Unknown";

        // Column widths from dbo.ActivityLogs. Audit rows are written on paths that are
        // already failing (a rejected sign-in, a replayed token), so an over-long
        // User-Agent must never be what turns that into a 500 — truncate instead.
        private const int MaxUserAgentLength = 200;
        private const int MaxIpAddressLength = 50;
        private const int MaxDetailLength = 300;

        /// <summary>
        /// The only supported way to build an audit row. Callers pass the batch context
        /// (<paramref name="batchId"/> / <paramref name="executionOrder"/>) their service
        /// method is accumulating, so every write in one call reads back as one ordered batch.
        /// </summary>
        /// <param name="batchId">Shared by every row the calling service method emits.</param>
        /// <param name="executionOrder">1-based; pre-increment a counter per row.</param>
        /// <param name="executedBy">Null when the actor is genuinely unknown — never Guid.Empty.</param>
        /// <param name="ipAddress">From IClientContextAccessor, never from a request body.</param>
        /// <param name="userAgent">From IClientContextAccessor, never from a request body.</param>
        /// <param name="detail">Built with <see cref="BuildDetail"/>; falls back to the activity's own sentence.</param>
        /// <param name="referenceType">The entity kind acted on; null only for system-wide actions.</param>
        /// <param name="reference">That entity's primary key; null only for system-wide actions.</param>
        public static ActivityLog Create(
            Guid batchId,
            int executionOrder,
            ActivityEnum activity,
            Guid? executedBy,
            DateTime executedOn,
            string? ipAddress,
            string? userAgent,
            string? detail = null,
            ActivityLogReferenceTypeEnum? referenceType = null,
            Guid? reference = null)
        {
            return new ActivityLog
            {
                ActivityLogId = Guid.NewGuid(),
                BatchId = batchId,
                ExecutionOrder = executionOrder,
                Activity = (int)activity,
                ExecutedBy = executedBy,
                ExecutedOn = executedOn,
                IpAddress = Fit(ipAddress, UnknownClient, MaxIpAddressLength),
                UserAgent = Fit(userAgent, UnknownClient, MaxUserAgentLength),
                Detail = Fit(detail, activity.ToDetail(), MaxDetailLength),
                ReferenceType = referenceType.HasValue ? (int)referenceType.Value : null,
                Reference = reference,
            };
        }

        private static string Fit(string? value, string fallback, int maxLength)
        {
            var text = string.IsNullOrWhiteSpace(value) ? fallback : value;
            return text.Length <= maxLength ? text : text[..maxLength];
        }

        /// <summary>
        /// Central wording for an activity's detail sentence. Keeping the phrasing here —
        /// rather than at each call site — means one activity reads identically everywhere
        /// it is written. Activities with nothing to interpolate fall through to
        /// <see cref="ActivityEnumExtensions.ToDetail"/>.
        /// </summary>
        public static string BuildDetail(ActivityEnum activity, params object?[] args)
        {
            var first = args.Length > 0 ? args[0]?.ToString() : null;

            return activity switch
            {
                ActivityEnum.UserRegistered when first is not null
                    => $"User '{first}' was registered.",
                ActivityEnum.UserLoggedIn when first is not null
                    => $"User '{first}' signed in.",
                ActivityEnum.UserLoggedOut when first is not null
                    => $"User '{first}' signed out.",
                ActivityEnum.LoginFailed when first is not null
                    => $"Sign-in attempt failed for '{first}'.",
                ActivityEnum.AccountLocked when first is not null
                    => $"Account '{first}' was locked after {args.ElementAtOrDefault(1) ?? "repeated"} failed sign-in attempts.",
                ActivityEnum.AccountUnlocked when first is not null
                    => $"Account '{first}' was unlocked.",
                ActivityEnum.AccessTokenRefreshed when first is not null
                    => $"Access token was refreshed for '{first}'.",
                ActivityEnum.RefreshTokenReplayDetected when first is not null
                    => $"Refresh token replay detected for '{first}'; all active sessions were revoked.",
                ActivityEnum.UserUpdated when first is not null
                    => $"Profile for '{first}' was updated.",
                ActivityEnum.UserDeactivated when first is not null
                    => $"User '{first}' was deactivated.",
                ActivityEnum.UserReactivated when first is not null
                    => $"User '{first}' was reactivated.",
                ActivityEnum.UserInvited when first is not null
                    => $"User '{first}' was invited to set a password.",
                ActivityEnum.InvitationResent when first is not null
                    => $"Invitation for '{first}' was sent again.",
                ActivityEnum.UserActivated when first is not null
                    => $"Account '{first}' was activated and a password was set.",
                ActivityEnum.PasswordResetRequested when first is not null
                    => $"A password reset was requested for '{first}'.",
                ActivityEnum.PasswordResetCompleted when first is not null
                    => $"Password for '{first}' was reset.",
                ActivityEnum.InstallmentSchemeCreated when first is not null
                    => $"Installment scheme '{first}' was created.",
                ActivityEnum.InstallmentSchemeUpdated when first is not null
                    => $"Terms of installment scheme '{first}' were changed.",
                ActivityEnum.InstallmentSchemeDeactivated when first is not null
                    => $"Installment scheme '{first}' was withdrawn from offer.",
                ActivityEnum.InstallmentSchemeReactivated when first is not null
                    => $"Installment scheme '{first}' was put back on offer.",
                ActivityEnum.InvestmentSchemeCreated when first is not null
                    => $"Investment scheme '{first}' was created.",
                ActivityEnum.InvestmentSchemeUpdated when first is not null
                    => $"Terms of investment scheme '{first}' were changed.",
                ActivityEnum.InvestmentSchemeDeactivated when first is not null
                    => $"Investment scheme '{first}' was withdrawn from offer.",
                ActivityEnum.InvestmentSchemeReactivated when first is not null
                    => $"Investment scheme '{first}' was put back on offer.",

                // The second argument is what the registrant said they came for. It is
                // an enum's label, never their own words: this string is the audit
                // trail's, and free text from an anonymous form has no business in it.
                ActivityEnum.UserSelfRegistered when first is not null
                    => $"User '{first}' opened an account from the public site "
                     + $"({args.ElementAtOrDefault(1) ?? "no stated interest"}).",

                _ => activity.ToDetail(),
            };
        }
    }
}
