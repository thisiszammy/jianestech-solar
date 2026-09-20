namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// The signed-in identity as the UI needs it. Deliberately carries no credential
    /// material — <c>Password</c> and <c>Salt</c> never cross the service boundary.
    /// </summary>
    public class AuthenticatedUserDTO
    {
        public Guid UserId { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string? PhoneNumber { get; set; }

        public int UserType { get; set; }

        public string UserTypeLabel { get; set; } = string.Empty;

        public DateTime? LastSignInOn { get; set; }
    }
}
