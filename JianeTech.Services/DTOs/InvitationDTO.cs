namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// What the set-a-password page is allowed to know about the person holding an
    /// invitation link. Enough to greet them and to prove the link belongs to a real
    /// account — and nothing that would turn a guessed token into a way of reading the
    /// register. The email address in particular stays out: the recipient already has it,
    /// and echoing it would let anyone holding a token learn an address.
    /// </summary>
    public class InvitationDTO
    {
        public string FirstName { get; set; } = string.Empty;

        public string FullName { get; set; } = string.Empty;

        public string Username { get; set; } = string.Empty;

        /// <summary>When the link stops working, so the page can say so rather than fail later.</summary>
        public DateTime ExpiresOn { get; set; }
    }
}
