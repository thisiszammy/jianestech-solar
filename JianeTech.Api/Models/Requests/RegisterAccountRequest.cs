using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The registration form as it arrives from the public site.
/// </summary>
/// <remarks>
/// <para>
/// The same fields <c>InviteUserRequest</c> carries, minus the one that matters: there is
/// no <c>UserType</c>. This body arrives from an anonymous caller, so a role on it would
/// be a request for a role — <c>SelfRegisterUserAsync</c> fixes it to
/// <c>UserTypeEnum.User</c> and never asks. Adding the property back here would hand an
/// administrator account to anyone who could post JSON.
/// </para>
/// <para>
/// No password either, for the same reason the invitation carries none: the link this
/// sends is where a credential gets chosen, by the person it belongs to.
/// </para>
/// </remarks>
public class RegisterAccountRequest
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

    /// <summary>
    /// A <c>RegistrationInterestEnum</c> value. Optional — 0 is <c>Unspecified</c>, which
    /// is a real answer rather than a missing one. An unrecognised number is read as
    /// <c>Unspecified</c> too; this decides one clause of an audit sentence, and a stray
    /// integer is not worth a 400.
    /// </summary>
    public int Interest { get; set; }
}
