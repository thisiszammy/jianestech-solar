namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Which side of the fund somebody said they came for when they opened an account
    /// from the public site.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not a column. Nothing in <c>dbo.Users</c> records it, because the fund's
    /// two sides are agreements — <c>dbo.Investments</c> and <c>dbo.Installments</c> —
    /// and a stated interest is not one. It is kept as a stated intention in the
    /// account's audit row, which is the honest place for it: whoever follows the
    /// registration up can read what the person said they wanted, without the register
    /// claiming they are an investor before anything has been signed.
    /// </para>
    /// <para>
    /// An enum rather than free text because the value reaches
    /// <c>ActivityLog.BuildDetail</c>, and an anonymous form must not be able to write
    /// its own sentences into the audit trail.
    /// </para>
    /// </remarks>
    public enum RegistrationInterestEnum
    {
        /// <summary>Nothing was chosen. The question is optional and this is a real answer.</summary>
        Unspecified = 0,

        /// <summary>Wants to place capital with the fund.</summary>
        Investing = 1,

        /// <summary>Wants solar fitted and paid off on an instalment scheme.</summary>
        Installation = 2,
    }
}
