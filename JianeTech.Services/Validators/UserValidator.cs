using System.Diagnostics.CodeAnalysis;
using JianeTech.Data.Entities;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Validators
{
    /// <summary>
    /// All validation for the <c>User</c> resource. Stateless: registered as a singleton
    /// and shared across every request, so nothing may be held in a field.
    /// </summary>
    public class UserValidator
    {
        // Mirrors the column widths of dbo.Users. Keeping these in step with the schema is
        // what makes an over-long value a 400 with a readable message instead of a 500 from
        // SQL Server truncating the insert.
        private const int MaxNameLength = 70;
        private const int MaxUsernameLength = 50;
        private const int MaxEmailLength = 60;
        private const int MaxPhoneLength = 11;

        /// <summary>
        /// Uniqueness is checked against live rows only, matching the filtered unique
        /// indexes — a retired account must not permanently reserve its username.
        /// </summary>
        public async Task ValidateRegisterUser(
            User newUser,
            string password,
            IQueryable<User> existingUsers,
            CancellationToken cancellationToken)
        {
            RequireAccountFields(newUser);

            ValidatePassword(password);

            var usernameTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.Username == newUser.Username,
                cancellationToken);

            if (usernameTaken)
                throw new UserValidationException($"Username '{newUser.Username}' is already taken.");

            var emailTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.Email == newUser.Email,
                cancellationToken);

            if (emailTaken)
                throw new UserValidationException($"Email '{newUser.Email}' is already registered.");
        }

        /// <summary>
        /// The invitation form's rules: registration without the password, because the
        /// invitee chooses that themselves from the link they are about to be sent.
        /// Uniqueness is checked against live rows only, exactly as registration does.
        /// </summary>
        public async Task ValidateInviteUser(
            User newUser,
            IQueryable<User> existingUsers,
            CancellationToken cancellationToken)
        {
            RequireAccountFields(newUser);

            var usernameTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.Username == newUser.Username,
                cancellationToken);

            if (usernameTaken)
                throw new UserValidationException($"Username '{newUser.Username}' is already taken.");

            var emailTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.Email == newUser.Email,
                cancellationToken);

            if (emailTaken)
                throw new UserValidationException($"Email '{newUser.Email}' is already registered.");
        }

        /// <summary>
        /// The public registration form's rules: the invitation form's, with the role
        /// removed and nobody signed in behind it.
        /// </summary>
        /// <remarks>
        /// One difference, and it is the whole reason this is a separate method. A clash
        /// on the email address raises <see cref="DuplicateAccountException"/> rather
        /// than the plain exception, so the anonymous endpoint can answer without
        /// confirming that the address is on file — see that type for why. A clash on the
        /// username stays plain: it has to be reported or the person cannot finish the
        /// form, and a username is something they just invented rather than an identifier
        /// somebody else could be probing for.
        /// </remarks>
        public async Task ValidateSelfRegistration(
            User newUser,
            IQueryable<User> existingUsers,
            CancellationToken cancellationToken)
        {
            RequireAccountFields(newUser);

            var usernameTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.Username == newUser.Username,
                cancellationToken);

            if (usernameTaken)
                throw new UserValidationException(
                    $"Username '{newUser.Username}' is already taken. Choose another one.");

            var emailTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.Email == newUser.Email,
                cancellationToken);

            // Worded for the log, not for the person who submitted the form. The endpoint
            // catches this and answers everyone the same sentence.
            if (emailTaken)
                throw new DuplicateAccountException(
                    $"Email '{newUser.Email}' already has an account; no second one was created.");
        }

        /// <summary>
        /// The edit form's rules. Same shape as registration minus the password, plus the
        /// one difference that matters: uniqueness must ignore the row being edited, or
        /// saving a profile without touching its username would report the account as a
        /// clash with itself.
        /// </summary>
        public async Task ValidateUpdateUser(
            [NotNull] User? user,
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            IQueryable<User> existingUsers,
            CancellationToken cancellationToken)
        {
            if (user is null)
                throw new UserValidationException("That account no longer exists.");

            var trimmedFirstName = firstName?.Trim() ?? string.Empty;
            var trimmedLastName = lastName?.Trim() ?? string.Empty;
            var trimmedUsername = username?.Trim() ?? string.Empty;
            var trimmedEmail = email?.Trim() ?? string.Empty;
            var trimmedPhone = phoneNumber?.Trim() ?? string.Empty;

            RequireText(trimmedFirstName, "First name", MaxNameLength);
            RequireText(trimmedLastName, "Last name", MaxNameLength);
            RequireText(trimmedUsername, "Username", MaxUsernameLength);
            RequireText(trimmedEmail, "Email", MaxEmailLength);

            if (!string.IsNullOrWhiteSpace(trimmedPhone) && trimmedPhone.Length > MaxPhoneLength)
                throw new UserValidationException($"Phone number must be {MaxPhoneLength} characters or fewer.");

            if (!trimmedEmail.Contains('@', StringComparison.Ordinal))
                throw new UserValidationException("Email must be a valid address.");

            var userId = user.UserId;

            var usernameTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.UserId != userId && u.Username == trimmedUsername,
                cancellationToken);

            if (usernameTaken)
                throw new UserValidationException($"Username '{trimmedUsername}' is already taken.");

            var emailTaken = await existingUsers.AnyAsync(
                u => u.DeletedOn == null && u.UserId != userId && u.Email == trimmedEmail,
                cancellationToken);

            if (emailTaken)
                throw new UserValidationException($"Email '{trimmedEmail}' is already registered.");
        }

        /// <summary>
        /// Guards the register's on/off switch. The self-deactivation check is the
        /// load-bearing one: an inactive account is refused at sign-in *and* at token
        /// refresh, so an administrator who switched themselves off would be locked out of
        /// the only console that could switch them back on.
        /// </summary>
        public void ValidateSetUserActive([NotNull] User? user, bool isActive, Guid? actingUserId)
        {
            if (user is null)
                throw new UserValidationException("That account no longer exists.");

            if (!isActive && actingUserId is not null && actingUserId == user.UserId)
                throw new UserValidationException(
                    "You cannot deactivate your own account. Ask another administrator to do it.");

            if (user.IsActive == isActive)
                throw new UserValidationException(
                    $"'{user.Username}' is already {(isActive ? "active" : "inactive")}.");
        }

        public void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new UserValidationException("Password is required.");

            if (password.Length < PasswordRules.MinimumLength)
                throw new UserValidationException(PasswordRules.Describe());

            if (password.Length > PasswordRules.MaximumLength)
                throw new UserValidationException(
                    $"Password must be {PasswordRules.MaximumLength} characters or fewer.");
        }

        /// <summary>
        /// The field rules every way of creating an account shares: the four required
        /// columns at their widths, the optional phone at its, and an address that at
        /// least looks like one. Registration, invitation and self-registration differ
        /// only in what they do about a password and how they report a clash, so the
        /// part they agree on is written once.
        /// </summary>
        private static void RequireAccountFields(User newUser)
        {
            RequireText(newUser.FirstName, "First name", MaxNameLength);
            RequireText(newUser.LastName, "Last name", MaxNameLength);
            RequireText(newUser.Username, "Username", MaxUsernameLength);
            RequireText(newUser.Email, "Email", MaxEmailLength);

            if (!string.IsNullOrWhiteSpace(newUser.PhoneNumber) && newUser.PhoneNumber.Length > MaxPhoneLength)
                throw new UserValidationException($"Phone number must be {MaxPhoneLength} characters or fewer.");

            if (!newUser.Email.Contains('@', StringComparison.Ordinal))
                throw new UserValidationException("Email must be a valid address.");
        }

        private static void RequireText(string? value, string field, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new UserValidationException($"{field} is required.");

            if (value.Length > maxLength)
                throw new UserValidationException($"{field} must be {maxLength} characters or fewer.");
        }
    }
}
