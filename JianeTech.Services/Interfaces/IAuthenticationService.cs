using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    public interface IAuthenticationService
    {
        /// <summary>
        /// Verifies credentials and opens a session. Throws
        /// <c>AuthenticationValidationException</c> for bad credentials and
        /// <c>AccountLockedException</c> once the lock-out threshold is reached.
        /// </summary>
        Task<AuthenticationDTO> AuthenticateUserAsync(
            string username,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken);

        /// <summary>
        /// Exchanges a refresh grant for a fresh access token, rotating the grant when it
        /// is short-lived. Throws <c>RefreshTokenReplayException</c> — after revoking every
        /// live grant for that user — if the presented token was already consumed.
        /// </summary>
        Task<AuthenticationDTO> RefreshTokenAsync(
            Guid refreshTokenId,
            CancellationToken cancellationToken);

        /// <summary>
        /// Revokes the grant behind the presented cookie. Silently no-ops when the token is
        /// unknown, already closed, or does not belong to <paramref name="actingUserId"/> —
        /// signing out must never report on tokens the caller does not own.
        /// </summary>
        Task LogoutAsync(
            Guid refreshTokenId,
            Guid? actingUserId,
            CancellationToken cancellationToken);

        Task<AuthenticatedUserDTO?> GetAuthenticatedUserDTOById(
            Guid userId,
            CancellationToken cancellationToken);
    }
}
