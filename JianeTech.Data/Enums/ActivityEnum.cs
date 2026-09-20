namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Every write the platform performs has an entry here. Values are a persisted
    /// contract — append new members, never renumber or repurpose an existing one.
    /// </summary>
    public enum ActivityEnum
    {
        UserRegistered = 0,
        UserLoggedIn = 1,
        UserLoggedOut = 2,
        LoginFailed = 3,
        AccountLocked = 4,
        AccountUnlocked = 5,
        AccessTokenRefreshed = 6,
        RefreshTokenReplayDetected = 7,
        UserUpdated = 8,
        UserDeactivated = 9,
        UserReactivated = 10,
        UserInvited = 11,
        InvitationResent = 12,
        UserActivated = 13,
        PasswordResetRequested = 14,
        PasswordResetCompleted = 15,
        InstallmentSchemeCreated = 16,
        InstallmentSchemeUpdated = 17,
        InstallmentSchemeDeactivated = 18,
        InstallmentSchemeReactivated = 19,
        InvestmentSchemeCreated = 20,
        InvestmentSchemeUpdated = 21,
        InvestmentSchemeDeactivated = 22,
        InvestmentSchemeReactivated = 23,

        /// <summary>
        /// An account opened from the public site by the person it belongs to, rather
        /// than created for them by an administrator. Distinct from
        /// <see cref="UserInvited"/> because the two differ in who acted: an invitation
        /// names the administrator who issued it, and this names nobody at the fund.
        /// </summary>
        UserSelfRegistered = 24,
    }
}
