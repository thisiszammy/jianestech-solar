namespace JianeTech.Data.Enums
{
    /// <summary>
    /// Presentation wording for <see cref="UserTypeEnum"/>. Kept beside the enum, the same
    /// way <see cref="ActivityEnumExtensions"/> is, so a new member shows up here as a
    /// compile-time gap rather than as a raw enum name leaking into the console.
    /// </summary>
    public static class UserTypeEnumExtensions
    {
        /// <summary>What the console calls this role.</summary>
        public static string ToLabel(this UserTypeEnum userType) => userType switch
        {
            UserTypeEnum.SystemAdmin => "Administrator",
            UserTypeEnum.User        => "Standard user",
            _                        => userType.ToString(),
        };
    }
}
