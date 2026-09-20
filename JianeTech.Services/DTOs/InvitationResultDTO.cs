namespace JianeTech.Services.DTOs
{
    /// <summary>
    /// The outcome of inviting someone: the account, and whether the mail carrying their
    /// link actually left.
    /// </summary>
    /// <remarks>
    /// The two are reported separately because they can genuinely disagree. The account is
    /// committed before Brevo is called, so a mail failure leaves a real, usable row with a
    /// live invitation against it — the recoverable state, and exactly what
    /// <c>ResendInvitationAsync</c> exists for. Collapsing that into a single success flag
    /// would either hide a mail that never arrived or roll back an account that is fine.
    /// </remarks>
    public class InvitationResultDTO
    {
        public Guid UserId { get; set; }

        public bool EmailSent { get; set; }

        /// <summary>The delivery failure in the words the console should show. Null on success.</summary>
        public string? EmailError { get; set; }
    }
}
