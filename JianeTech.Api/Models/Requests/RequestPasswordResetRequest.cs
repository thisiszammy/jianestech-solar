using System.ComponentModel.DataAnnotations;

namespace JianeTech.Api.Models.Requests;

/// <summary>
/// The forgot-password form. One field, because asking for both a username and an email
/// would make the reader guess which one the account was filed under.
/// </summary>
public class RequestPasswordResetRequest
{
    /// <summary>A username or an email address; the service accepts either.</summary>
    [Required(ErrorMessage = "Enter your username or email address.")]
    public string Identifier { get; set; } = string.Empty;
}
