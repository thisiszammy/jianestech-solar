using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The edit-account form as it arrives from the console. No password: changing a
/// credential is a separate act. No acting-user id either — who is making the change is
/// read from the bearer token, never accepted from the body.
/// </summary>
public class UpdateUserRequest
{
    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>A <c>UserTypeEnum</c> value.</summary>
    public int UserType { get; set; }
}
