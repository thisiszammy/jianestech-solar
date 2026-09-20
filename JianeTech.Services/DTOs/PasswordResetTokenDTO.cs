namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// What the choose-a-new-password page is allowed to know about the holder of a
    /// recovery link. Same discipline as <see cref="InvitationDTO"/>: enough to greet the
    /// reader and prove the link is live, nothing that would make a guessed token a way of
    /// reading the register.
    /// </summary>
    public class PasswordResetTokenDTO
    {
        public string FirstName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        public DateTime ExpiresOn { get; set; }
    }
}
