using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The password chosen at the end of an invitation or a recovery link. The token itself
/// travels in the route, not here.
/// </summary>
public class SetPasswordRequest
{
    [Required(ErrorMessage = "Password is required.")]
    public string Password { get; set; } = string.Empty;
}
