namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Presentation wording for <see cref="ActivityEnum"/>. Kept beside the enum so a
    /// new member shows up here as a compile-time gap rather than as a raw enum name
    /// leaking into the activity feed.
    /// </summary>
    public static class ActivityEnumExtensions
    {
        /// <summary>Sentence written into ActivityLog.Detail when no argument-aware override applies.</summary>
        public static string ToDetail(this ActivityEnum activity) => activity switch
        {
            ActivityEnum.UserRegistered             => "User registered.",
            ActivityEnum.UserLoggedIn               => "User logged in.",
            ActivityEnum.UserLoggedOut              => "User logged out.",
            ActivityEnum.LoginFailed                => "User login attempt failed.",
            ActivityEnum.AccountLocked              => "Account was locked after repeated failed logins.",
            ActivityEnum.AccountUnlocked            => "Account was unlocked.",
            ActivityEnum.AccessTokenRefreshed       => "Access token was refreshed.",
            ActivityEnum.RefreshTokenReplayDetected => "Refresh token replay detected; all sessions were revoked.",
            ActivityEnum.UserUpdated                => "User profile was updated.",
            ActivityEnum.UserDeactivated            => "User was deactivated.",
            ActivityEnum.UserReactivated            => "User was reactivated.",
            ActivityEnum.UserInvited                => "User was invited to set a password.",
            ActivityEnum.InvitationResent           => "Invitation was sent again.",
            ActivityEnum.UserActivated              => "Account was activated and a password set.",
            ActivityEnum.PasswordResetRequested     => "Password reset was requested.",
            ActivityEnum.PasswordResetCompleted     => "Password was reset.",
            ActivityEnum.InstallmentSchemeCreated   => "Installment scheme was created.",
            ActivityEnum.InstallmentSchemeUpdated   => "Installment scheme terms were changed.",
            ActivityEnum.InstallmentSchemeDeactivated
                                                    => "Installment scheme was withdrawn from offer.",
            ActivityEnum.InstallmentSchemeReactivated
                                                    => "Installment scheme was put back on offer.",
            ActivityEnum.InvestmentSchemeCreated    => "Investment scheme was created.",
            ActivityEnum.InvestmentSchemeUpdated    => "Investment scheme terms were changed.",
            ActivityEnum.InvestmentSchemeDeactivated
                                                    => "Investment scheme was withdrawn from offer.",
            ActivityEnum.InvestmentSchemeReactivated
                                                    => "Investment scheme was put back on offer.",
            ActivityEnum.UserSelfRegistered         => "Account was opened from the public site.",
            _                                       => activity.ToString(),
        };

        /// <summary>Short column label for the activity tables.</summary>
        public static string ToLabel(this ActivityEnum activity) => activity switch
        {
            ActivityEnum.UserRegistered             => "User registered",
            ActivityEnum.UserLoggedIn               => "Sign in",
            ActivityEnum.UserLoggedOut              => "Sign out",
            ActivityEnum.LoginFailed                => "Sign in failed",
            ActivityEnum.AccountLocked              => "Account locked",
            ActivityEnum.AccountUnlocked            => "Account unlocked",
            ActivityEnum.AccessTokenRefreshed       => "Token refreshed",
            ActivityEnum.RefreshTokenReplayDetected => "Token replay",
            ActivityEnum.UserUpdated                => "User updated",
            ActivityEnum.UserDeactivated            => "User deactivated",
            ActivityEnum.UserReactivated            => "User reactivated",
            ActivityEnum.UserInvited                => "User invited",
            ActivityEnum.InvitationResent           => "Invitation resent",
            ActivityEnum.UserActivated              => "Account activated",
            ActivityEnum.PasswordResetRequested     => "Reset requested",
            ActivityEnum.PasswordResetCompleted     => "Password reset",
            ActivityEnum.InstallmentSchemeCreated   => "Installment scheme created",
            ActivityEnum.InstallmentSchemeUpdated   => "Installment scheme updated",
            ActivityEnum.InstallmentSchemeDeactivated
                                                    => "Installment scheme withdrawn",
            ActivityEnum.InstallmentSchemeReactivated
                                                    => "Installment scheme restored",
            ActivityEnum.InvestmentSchemeCreated    => "Investment scheme created",
            ActivityEnum.InvestmentSchemeUpdated    => "Investment scheme updated",
            ActivityEnum.InvestmentSchemeDeactivated
                                                    => "Investment scheme withdrawn",
            ActivityEnum.InvestmentSchemeReactivated
                                                    => "Investment scheme restored",
            ActivityEnum.UserSelfRegistered         => "Account opened",
            _                                       => activity.ToString(),
        };

        /// <summary>Tone the UI paints the row with: success / failed / blocked.</summary>
        public static string ToStatus(this ActivityEnum activity) => activity switch
        {
            ActivityEnum.LoginFailed                => "failed",
            ActivityEnum.AccountLocked              => "blocked",
            ActivityEnum.RefreshTokenReplayDetected => "blocked",
            _                                       => "success",
        };
    }
}
