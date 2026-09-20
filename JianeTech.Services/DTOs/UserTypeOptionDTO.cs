namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// One entry of the role picker. Served rather than restated in the client for the
    /// same reason as <see cref="ActivityOptionDTO"/>: the wording belongs to
    /// <c>UserTypeEnumExtensions</c>, and a role appended to the enum should appear in the
    /// form without anyone remembering to add it twice.
    /// </summary>
    public class UserTypeOptionDTO
    {
        public int UserType { get; set; }

        public string Label { get; set; } = string.Empty;
    }
}
