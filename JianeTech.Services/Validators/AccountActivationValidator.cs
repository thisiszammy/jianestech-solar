using System.Diagnostics.CodeAnalysis;
using JianeTech.Data.Entities;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Security;

namespace JianeTech.Services.Validators
{
    /// <summary>
    /// All validation for the account-activation resource. Stateless: registered as a
    /// singleton and shared across every request, so nothing may be held in a field.
    /// </summary>
    public class AccountActivationValidator
    {
        /// <summary>
        /// Guards a link arriving from someone's inbox. Every branch says the same kind of
        /// thing — the link no longer works, ask for a new one — because a stranger holding
        /// a guessed token must not be able to tell "no such token" from "that one was used
        /// an hour ago". The wording differs only where it tells a legitimate holder
        /// something they can act on.
        /// </summary>
        /// <remarks>
        /// <paramref name="token"/> is looked up by the <i>hash</i> of the Guid in the
        /// link, so reaching this method at all means the caller presented a real one.
        /// </remarks>
        public void ValidateActivationToken([NotNull] UserActivationToken? token, DateTime now)
        {
            if (token is null)
                throw new AccountActivationException(
                    "This invitation link is not valid. Ask an administrator to send a new one.");

            if (token.UsedOn is not null)
                throw new AccountActivationException(
                    "This invitation has already been used. Sign in, or reset your password if you have forgotten it.");

            if (token.InvalidatedOn is not null)
                throw new AccountActivationException(
                    "This invitation has been replaced by a newer one. Use the most recent email you were sent.");

            if (token.ExpiredOn is null || token.ExpiredOn <= now)
                throw new AccountActivationException(
                    "This invitation has expired. Ask an administrator to send a new one.");

            if (token.User is null || token.User.DeletedOn is not null)
                throw new AccountActivationException(
                    "This invitation is no longer valid. Ask an administrator to send a new one.");

            // Deactivation is a decision somebody made, and activating would quietly undo
            // it — the account would end up with a working password and no way in, or worse,
            // a way in nobody meant to grant.
            if (!token.User.IsActive)
                throw new AccountActivationException(
                    "This account has been switched off. Ask an administrator to reactivate it first.");
        }

        /// <summary>
        /// Guards the register's resend button. An account that has already set a password
        /// is refused here rather than silently re-invited: re-inviting would mint a link
        /// that can overwrite a working credential, which is a password reset wearing the
        /// wrong name — and the register has a button for that.
        /// </summary>
        public void ValidateResendInvitation([NotNull] User? user, bool hasActivated)
        {
            if (user is null)
                throw new AccountActivationException("That account no longer exists.");

            if (hasActivated)
                throw new AccountActivationException(
                    $"'{user.Username}' has already set a password. Send a password reset instead.");

            if (!user.IsActive)
                throw new AccountActivationException(
                    $"'{user.Username}' is inactive. Reactivate the account before inviting them again.");
        }

        /// <summary>
        /// The same policy <c>UserValidator</c> enforces, restated against this service's
        /// own exception type. §F: a validator throws only its service's exception, so the
        /// controller's catch list stays an accurate description of what can come back.
        /// </summary>
        public void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new AccountActivationException("Password is required.");

            if (password.Length < PasswordRules.MinimumLength)
                throw new AccountActivationException(PasswordRules.Describe());

            if (password.Length > PasswordRules.MaximumLength)
                throw new AccountActivationException(
                    $"Password must be {PasswordRules.MaximumLength} characters or fewer.");
        }
    }
}
