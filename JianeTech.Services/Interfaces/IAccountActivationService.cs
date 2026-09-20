using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    /// <summary>
    /// The invitation's second half. <c>IUserService.InviteUserAsync</c> creates the
    /// account and issues the first link; everything that happens to that link afterwards
    /// — opening it, spending it, replacing it — lives here.
    /// </summary>
    public interface IAccountActivationService
    {
        /// <summary>
        /// Who the link belongs to, for the set-a-password page to greet and to check
        /// before it renders a form that cannot be submitted.
        /// Throws <c>AccountActivationException</c>.
        /// </summary>
        Task<InvitationDTO> GetInvitationDTOByToken(Guid token, CancellationToken cancellationToken);

        /// <summary>
        /// Spends the link: sets the password the invitee chose and closes the token
        /// against reuse. Throws <c>AccountActivationException</c>.
        /// </summary>
        Task ActivateAccountAsync(Guid token, string password, CancellationToken cancellationToken);

        /// <summary>
        /// Issues a fresh invitation and voids any still outstanding, then mails it.
        /// Throws <c>AccountActivationException</c> and <c>EmailDeliveryException</c>.
        /// </summary>
        Task ResendInvitationAsync(Guid userId, Guid? actingUserId, CancellationToken cancellationToken);
    }
}
