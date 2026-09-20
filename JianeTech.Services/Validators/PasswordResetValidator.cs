using System.Diagnostics.CodeAnalysis;
using JianeTech.Data.Entities;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Security;

namespace JianeTech.Services.Validators
{
    /// <summary>
    /// All validation for the password-reset resource. Stateless: registered as a
    /// singleton and shared across every request, so nothing may be held in a field.
    /// </summary>
    public class PasswordResetValidator
    {
        /// <summary>
        /// Guards a recovery link arriving from someone's inbox. Looked up by the hash of
        /// the Guid in the link, so reaching this method means a real token was presented.
        /// </summary>
        public void ValidateResetToken([NotNull] PasswordResetToken? token, DateTime now)
        {
            if (token is null)
                throw new PasswordResetException(
                    "This reset link is not valid. Request a new one from the sign-in page.");

            if (token.UsedOn is not null)
                throw new PasswordResetException(
                    "This reset link has already been used. Request a new one if you still need it.");

            if (token.InvalidatedOn is not null)
                throw new PasswordResetException(
                    "This reset link has been replaced by a newer one. Use the most recent email you were sent.");

            if (token.ExpiredOn is null || token.ExpiredOn <= now)
                throw new PasswordResetException(
                    "This reset link has expired. Request a new one from the sign-in page.");

            if (token.User is null || token.User.DeletedOn is not null)
                throw new PasswordResetException(
                    "This reset link is no longer valid. Request a new one from the sign-in page.");

            if (!token.User.IsActive)
                throw new PasswordResetException(
                    "This account has been switched off. Ask an administrator to reactivate it.");
        }

        /// <summary>
        /// Guards the register's "send a reset" button. Unlike the anonymous request — which
        /// is deliberately silent about whether an account exists — this one is operated by
        /// someone already looking at the row, so it can say plainly why it refused.
        /// </summary>
        public void ValidateSendPasswordReset([NotNull] User? user, bool hasActivated)
        {
            if (user is null)
                throw new PasswordResetException("That account no longer exists.");

            if (!user.IsActive)
                throw new PasswordResetException(
                    $"'{user.Username}' is inactive and cannot sign in. Reactivate the account first.");

            // Someone still holding an unused invitation has no password to reset. Sending
            // a reset would work, but it would land beside an invitation that also still
            // works, and the two links would race to set the same credential.
            if (!hasActivated)
                throw new PasswordResetException(
                    $"'{user.Username}' has not set a password yet. Resend their invitation instead.");
        }

        /// <summary>
        /// The same policy <c>UserValidator</c> enforces, restated against this service's
        /// own exception type — see the note on <c>AccountActivationValidator</c>.
        /// </summary>
        public void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new PasswordResetException("Password is required.");

            if (password.Length < PasswordRules.MinimumLength)
                throw new PasswordResetException(PasswordRules.Describe());

            if (password.Length > PasswordRules.MaximumLength)
                throw new PasswordResetException(
                    $"Password must be {PasswordRules.MaximumLength} characters or fewer.");
        }
    }
}
