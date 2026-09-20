using System.Diagnostics.CodeAnalysis;
using JianeTech.Data.Entities;
using JianeTech.Services.Exceptions;

namespace JianeTech.Services.Validators
{
    /// <summary>
    /// All cross-row checks for the sign-in and rotation flows. Stateless: registered as a
    /// singleton and shared across every request, so nothing may be held in a field.
    /// </summary>
    public class AuthenticationValidator
    {
        /// <summary>
        /// Deliberately identical for "no such user" and "user is not active". Any wording
        /// that distinguished the two would turn the sign-in form into a username oracle.
        /// </summary>
        public const string GenericCredentialError = "Invalid username or password.";

        public const string AccountLockedError =
            "This account is locked after too many failed sign-in attempts. Contact an administrator.";

        /// <summary>
        /// Gate that runs before the password is even hashed. Narrows a nullable lookup to
        /// a usable account for the caller.
        /// </summary>
        public void ValidateSignInCandidate([NotNull] User? user)
        {
            if (user is null)
                throw new AuthenticationValidationException(GenericCredentialError);

            if (user.DeletedOn is not null)
                throw new AuthenticationValidationException(GenericCredentialError);

            if (user.IsLocked)
                throw new AccountLockedException(AccountLockedError);

            if (!user.IsActive)
                throw new AuthenticationValidationException(GenericCredentialError);
        }

        /// <summary>
        /// Validates a presented refresh grant. A grant whose row is already consumed is a
        /// replay, not merely an expiry — it is reported separately so the caller can revoke
        /// every other live grant for that user before the exception escapes.
        /// </summary>
        public void ValidateRefreshToken([NotNull] AccessRefreshToken? token, DateTime now)
        {
            if (token is null)
                throw new AuthenticationValidationException("Session not found. Please sign in again.");

            if (token.UsedOn is not null)
                throw new RefreshTokenReplayException(
                    "This session has already been used. Please sign in again.",
                    token.UserId);

            if (token.InvalidatedOn is not null)
                throw new AuthenticationValidationException("This session has been signed out. Please sign in again.");

            if (token.ExpiredOn is null || token.ExpiredOn <= now)
                throw new AuthenticationValidationException("Your session has expired. Please sign in again.");

            if (token.User is null || token.User.DeletedOn is not null)
                throw new AuthenticationValidationException("This session is no longer valid. Please sign in again.");

            if (!token.User.IsActive || token.User.IsLocked)
                throw new AuthenticationValidationException("This session is no longer valid. Please sign in again.");
        }
    }
}
