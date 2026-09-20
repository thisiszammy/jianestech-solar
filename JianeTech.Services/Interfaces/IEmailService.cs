namespace JianeTech.Services.Interfaces
{
    /// <summary>
    /// Outbound transactional mail. The only implementation talks to Brevo, so every
    /// method here is an external API call — callers treat a failure as recoverable
    /// (the row is already committed; the link can be issued again) rather than fatal.
    /// </summary>
    /// <remarks>
    /// Raw token Guids are parameters here and nowhere else. They are what the recipient
    /// clicks, so they must reach the mail; they must not reach a DTO, a log line or the
    /// database, which stores only <see cref="Security.TokenHasher"/>'s SHA-256 of them.
    /// </remarks>
    public interface IEmailService
    {
        /// <summary>
        /// The invitation. Carries a one-time link to the set-a-password page.
        /// Throws <c>EmailDeliveryException</c>.
        /// </summary>
        Task SendActivationEmailAsync(
            string toEmail,
            string firstName,
            string fullName,
            string username,
            Guid activationToken,
            DateTime expiresOn,
            CancellationToken cancellationToken);

        /// <summary>
        /// The recovery mail. Same shape as the invitation, different template and route.
        /// Throws <c>EmailDeliveryException</c>.
        /// </summary>
        Task SendPasswordResetEmailAsync(
            string toEmail,
            string firstName,
            string fullName,
            string username,
            Guid resetToken,
            DateTime expiresOn,
            CancellationToken cancellationToken);
    }
}
