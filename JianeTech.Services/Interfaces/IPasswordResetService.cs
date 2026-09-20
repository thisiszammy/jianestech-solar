using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    /// <summary>
    /// Recovery for an account that already has a password. Someone still holding an
    /// unspent invitation belongs to <see cref="IAccountActivationService"/> instead —
    /// they have no credential to recover.
    /// </summary>
    public interface IPasswordResetService
    {
        /// <summary>
        /// The anonymous request, from the forgot-password page. Takes a username or an
        /// email address and, deliberately, <b>tells the caller nothing</b>: an unknown
        /// identifier, a locked account and a mail that failed to send are all silent
        /// successes, because any difference between them turns this endpoint into a way
        /// of finding out which accounts exist. Failures are recorded in the log instead.
        /// </summary>
        Task RequestPasswordResetAsync(string identifier, CancellationToken cancellationToken);

        /// <summary>
        /// The register's version, operated by an administrator looking at the row. Reports
        /// plainly why it refused, because the caller can already see the account.
        /// Throws <c>PasswordResetException</c> and <c>EmailDeliveryException</c>.
        /// </summary>
        Task SendPasswordResetAsync(Guid userId, Guid? actingUserId, CancellationToken cancellationToken);

        /// <summary>
        /// Who the link belongs to, for the choose-a-password page to greet and to check
        /// before it renders a form that cannot be submitted.
        /// Throws <c>PasswordResetException</c>.
        /// </summary>
        Task<PasswordResetTokenDTO> GetPasswordResetTokenDTOByToken(Guid token, CancellationToken cancellationToken);

        /// <summary>
        /// Spends the link: replaces the password and closes the token against reuse.
        /// Throws <c>PasswordResetException</c>.
        /// </summary>
        Task ResetPasswordAsync(Guid token, string password, CancellationToken cancellationToken);
    }
}
