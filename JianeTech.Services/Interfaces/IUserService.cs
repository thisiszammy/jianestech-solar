using JianeTech.Data.Enums;
using JianeTech.Services.DTOs;

namespace JianeTech.Services.Interfaces
{
    public interface IUserService
    {
        /// <summary>
        /// Creates an account with no password of its own and mails its new holder a
        /// one-time link to choose one. This is how the console adds people;
        /// <see cref="RegisterUserAsync"/> is for the seeder, which has a password to hand
        /// and no inbox to send to.
        /// </summary>
        /// <remarks>
        /// The account is committed before the mail is attempted, so the returned
        /// <see cref="InvitationResultDTO"/> reports the two outcomes separately — see the
        /// note on that type. Throws <c>UserValidationException</c>; a delivery failure is
        /// reported in the result rather than thrown.
        /// </remarks>
        Task<InvitationResultDTO> InviteUserAsync(
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            UserTypeEnum userType,
            Guid? createdBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Opens an account for a visitor who asked for one from the public site, and
        /// mails them the same one-time link an invitation carries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The twin of <see cref="InviteUserAsync"/>, and deliberately not a parameter on
        /// it. Two things differ and both are security decisions rather than options: the
        /// role is fixed to <c>UserTypeEnum.User</c> and is never read from the caller, so
        /// an anonymous request cannot mint itself an administrator; and there is no
        /// <c>createdBy</c>, because nobody at the fund created this account.
        /// </para>
        /// <para>
        /// Throws <c>UserValidationException</c> — including
        /// <c>DuplicateAccountException</c> when the address already has an account,
        /// which the anonymous endpoint catches first so its answer does not disclose
        /// that. A delivery failure is reported in the result rather than thrown, exactly
        /// as it is for an invitation.
        /// </para>
        /// </remarks>
        Task<InvitationResultDTO> SelfRegisterUserAsync(
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            RegistrationInterestEnum interest,
            CancellationToken cancellationToken);

        /// <summary>
        /// Creates an account with a password already chosen and returns its id. Used by
        /// first-run seeding, which has nobody to invite. The console invites instead — see
        /// <see cref="InviteUserAsync"/>. Throws <c>UserValidationException</c>.
        /// </summary>
        Task<Guid> RegisterUserAsync(
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            string password,
            UserTypeEnum userType,
            Guid? createdBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Rewrites an account's profile. The password is not touched here — changing a
        /// credential is a separate act with its own audit line.
        /// Throws <c>UserValidationException</c>.
        /// </summary>
        Task UpdateUserAsync(
            Guid userId,
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            UserTypeEnum userType,
            Guid? updatedBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// Turns console access on or off. Deactivation is what the register calls
        /// "delete": the row stays, its history stays, and the account simply cannot sign
        /// in — <c>AuthenticationValidator</c> refuses both a sign-in and a token refresh
        /// for an inactive user, so any live session dies at its next rotation.
        /// Throws <c>UserValidationException</c>.
        /// </summary>
        Task SetUserActiveAsync(
            Guid userId,
            bool isActive,
            Guid? updatedBy,
            CancellationToken cancellationToken);

        /// <summary>
        /// One page of the account register, newest first. Every filter is optional;
        /// passing none returns every live account paged. Retired (soft-deleted) accounts
        /// are never included.
        /// </summary>
        Task<QueryableEntityDto<UserDTO>> GetUserDTOs(
            int page,
            int pageSize,
            string? search,
            int? userType,
            bool? isActive,
            bool? isLocked,
            bool? isPending,
            CancellationToken cancellationToken);

        /// <summary>Every role an account can be given, worded for the console.</summary>
        List<UserTypeOptionDTO> GetUserTypeOptionDTOs();

        Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken);
    }
}
