using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

public class AuthenticateUserRequest
{
    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>Opts into a long-lived refresh grant that is reused rather than rotated.</summary>
    public bool RememberMe { get; set; }
}
