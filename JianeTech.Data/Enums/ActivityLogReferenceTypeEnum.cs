namespace JianeTech.Data.Enums
{
    /// <summary>
    /// The entity an <see cref="ActivityEnum"/> row points at. Paired with
    /// ActivityLog.Reference (that entity's primary key) so audit queries can
    /// target a single record. Nullable on system-wide actions that own no row.
    /// </summary>
    public enum ActivityLogReferenceTypeEnum
    {
        User = 0,
        AccessRefreshToken = 1,
        InstallmentScheme = 2,
        InvestmentScheme = 3,
    }
}
