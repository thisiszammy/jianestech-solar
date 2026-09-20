namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Presentation wording for <see cref="SchemeTypeEnum"/>. Kept beside the enum, the
    /// same way <see cref="UserTypeEnumExtensions"/> is, so a new member shows up here as
    /// a compile-time gap rather than as a raw enum name leaking into the console.
    /// </summary>
    /// <remarks>
    /// The two members differ in exactly one thing — when the investor takes delivery of
    /// the installation the scheme carries — and everything else about a scheme follows
    /// from it. The wording below says that difference out loud rather than repeating the
    /// enum's own name, because "ImmediateInstallation" tells a reader nothing they did
    /// not already have.
    /// </remarks>
    public static class SchemeTypeEnumExtensions
    {
        /// <summary>The one word the register prints in its Structure column.</summary>
        public static string ToLabel(this SchemeTypeEnum schemeType) => schemeType switch
        {
            SchemeTypeEnum.ImmediateInstallation => "Immediate",
            SchemeTypeEnum.DeferredInstallation  => "Deferred",
            _                                    => schemeType.ToString(),
        };

        /// <summary>
        /// The qualifying line under that word: where in the term the installation lands.
        /// This is the whole of the difference between the two types, so it is never left
        /// to the reader to remember which is which.
        /// </summary>
        public static string ToTiming(this SchemeTypeEnum schemeType) => schemeType switch
        {
            SchemeTypeEnum.ImmediateInstallation => "Installation up front",
            SchemeTypeEnum.DeferredInstallation  => "Installation at the end",
            _                                    => string.Empty,
        };
    }
}
