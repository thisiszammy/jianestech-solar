namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Presentation wording for <see cref="RegistrationInterestEnum"/>. Kept beside the
    /// enum, the same way <see cref="UserTypeEnumExtensions"/> is, so a new member shows
    /// up here as a compile-time gap rather than as a raw enum name leaking into an
    /// audit row.
    /// </summary>
    public static class RegistrationInterestEnumExtensions
    {
        /// <summary>
        /// How the audit trail names this interest. Written as the thing the person
        /// wants rather than as a label for the person, because at registration that is
        /// all that is known — nobody is an investor until a placement is agreed.
        /// </summary>
        public static string ToLabel(this RegistrationInterestEnum interest) => interest switch
        {
            RegistrationInterestEnum.Investing    => "interested in placing capital",
            RegistrationInterestEnum.Installation => "interested in having solar fitted",
            RegistrationInterestEnum.Unspecified  => "no stated interest",
            _                                     => interest.ToString(),
        };
    }
}
