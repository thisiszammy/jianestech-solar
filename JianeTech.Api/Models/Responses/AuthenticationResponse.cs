using JianeTech.Services.DTOs;

namespace JianeTech.Api.Models.Responses;

/// <summary>
/// What a successful sign-in or refresh returns to the browser. Note what is absent: the
/// access token itself is never in the body. It travels only in the HttpOnly cookie, so
/// the client learns when the session expires without ever being able to read the token.
/// </summary>
public class AuthenticationResponse
{
    public DateTime AccessTokenExpiresOn { get; set; }

    public required AuthenticatedUserDTO User { get; set; }
}
