using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The invite-a-user form as it arrives from the console.
/// </summary>
/// <remarks>
/// Two things are absent, both deliberately. There is no acting-user id — who is issuing
/// the invitation is read from the bearer token, never accepted from the body. And there
/// is no password: the invitee chooses their own from the link this sends, so no
/// credential is ever typed by one person on behalf of another, and none crosses this
/// wire at all.
/// </remarks>
public class InviteUserRequest
{
    [Required(ErrorMessage = "First name is required.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Optional: dbo.Users.PhoneNumber is NOT NULL but an empty string is valid.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>A <c>UserTypeEnum</c> value.</summary>
    public int UserType { get; set; }
}
