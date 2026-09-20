using JianeTech.Data.Entities;
using JianeTech.Data.Enums;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using JianeTech.Services.DTOs;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using JianeTech.Services.Security;
using JianeTech.Services.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// Password recovery, from both ends: the forgot-password form a signed-out person
    /// uses, and the button an administrator presses on a row in the register.
    /// </summary>
    /// <remarks>
    /// Same token discipline as <see cref="AccountActivationService"/> — the link carries a
    /// raw Guid, <c>dbo.PasswordResetTokens.TokenId</c> holds only its SHA-256, and the raw
    /// value goes to <see cref="IEmailService"/> and nowhere else.
    /// </remarks>
    public class PasswordResetService : IPasswordResetService
    {
        /// <summary>
        /// Much shorter than an invitation's week. A recovery link is requested by someone
        /// sitting at the form and waiting for the mail, so an hour is generous — and the
        /// thing it protects already exists, which an invitation's does not.
        /// </summary>
        public static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(1);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserRepository _userRepository;
        private readonly IPasswordResetTokenRepository _passwordResetTokenRepository;
        private readonly IUserActivationTokenRepository _activationTokenRepository;
        private readonly IAccessRefreshTokenRepository _accessRefreshTokenRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly IEmailService _emailService;
        private readonly PasswordResetValidator _validator;
        private readonly IClientContextAccessor _clientContextAccessor;
        private readonly IDateTimeService _dateTime;

        /// <summary>
        /// Injected for one job: recording a delivery failure on the anonymous path, where
        /// it cannot be reported to the caller without also reporting whether the account
        /// exists. The <c>[ServiceCaller]</c> lines around the Brevo call itself belong to
        /// <see cref="BrevoEmailService"/>, which is the service that actually makes it.
        /// </summary>
        private readonly ILogger<PasswordResetService> _logger;

        public PasswordResetService(
            IUnitOfWork unitOfWork,
            IUserRepository userRepository,
            IPasswordResetTokenRepository passwordResetTokenRepository,
            IUserActivationTokenRepository activationTokenRepository,
            IAccessRefreshTokenRepository accessRefreshTokenRepository,
            IActivityLogRepository activityLogRepository,
            IEmailService emailService,
            PasswordResetValidator validator,
            IClientContextAccessor clientContextAccessor,
            IDateTimeService dateTime,
            ILogger<PasswordResetService> logger)
        {
            _unitOfWork = unitOfWork;
            _userRepository = userRepository;
            _passwordResetTokenRepository = passwordResetTokenRepository;
            _activationTokenRepository = activationTokenRepository;
            _accessRefreshTokenRepository = accessRefreshTokenRepository;
            _activityLogRepository = activityLogRepository;
            _emailService = emailService;
            _validator = validator;
            _clientContextAccessor = clientContextAccessor;
            _dateTime = dateTime;
            _logger = logger;
        }

        public async Task RequestPasswordResetAsync(string identifier, CancellationToken cancellationToken)
        {
            var trimmed = identifier?.Trim() ?? string.Empty;
            if (trimmed.Length == 0)
            {
                return;
            }

            var user = await _userRepository.GetUsers()
                .FirstOrDefaultAsync(
                    u => u.DeletedOn == null
                         && u.IsActive
                         && (u.Username == trimmed || u.Email == trimmed),
                    cancellationToken);

            // Every refusal from here on is silent. The endpoint answers identically for an
            // address nobody has ever used and for one belonging to the finance director,
            // because any difference at all is a way of enumerating the register.
            if (user is null)
            {
                return;
            }

            var activationTokens = await _activationTokenRepository.GetUserActivationTokens()
                .Where(t => t.UserId == user.UserId)
                .ToListAsync(cancellationToken);

            // Nothing to reset yet: they are still holding an invitation. Sending a second
            // link here would race the first to set the same credential.
            if (!AccountActivationService.HasActivated(activationTokens))
            {
                _logger.LogInformation(
                    "Password reset requested for {UserId}, which has not been activated. No mail sent.",
                    user.UserId);
                return;
            }

            var issued = await IssueResetTokenAsync(user, actingUserId: user.UserId, cancellationToken);

            try
            {
                await SendResetEmailAsync(user, issued, cancellationToken);
            }
            catch (EmailDeliveryException ex)
            {
                // Swallowed on purpose, and only here. Letting this surface would mean an
                // unknown identifier returned 200 while a known one whose mail bounced
                // returned 400 — the enumeration oracle this whole method is shaped to
                // avoid. The administrator-operated path below reports it loudly instead.
                _logger.LogError(ex,
                    "Password reset mail failed for {UserId}. The token was issued and stands.",
                    user.UserId);
            }
        }

        public async Task SendPasswordResetAsync(
            Guid userId,
            Guid? actingUserId,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetUsers()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.DeletedOn == null, cancellationToken);

            var activationTokens = await _activationTokenRepository.GetUserActivationTokens()
                .Where(t => t.UserId == userId)
                .ToListAsync(cancellationToken);

            _validator.ValidateSendPasswordReset(user, AccountActivationService.HasActivated(activationTokens));

            var issued = await IssueResetTokenAsync(user, actingUserId, cancellationToken);

            // Unlike the anonymous path, a failure reaches the caller: an administrator is
            // watching, already knows the account exists, and needs to be told the mail did
            // not go. The audit row reads as issuing a link, so it stays true either way.
            await SendResetEmailAsync(user, issued, cancellationToken);
        }

        public async Task<PasswordResetTokenDTO> GetPasswordResetTokenDTOByToken(
            Guid token,
            CancellationToken cancellationToken)
        {
            var hashed = TokenHasher.Hash(token);

            // AsNoTracking: the page-load read writes nothing back. Spending the link is
            // ResetPasswordAsync's job and it loads its own tracked copy.
            var resetToken = await _passwordResetTokenRepository.GetPasswordResetTokens()
                .AsNoTracking()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenId == hashed, cancellationToken);

            _validator.ValidateResetToken(resetToken, _dateTime.Now());

            var user = resetToken.User;

            return new PasswordResetTokenDTO
            {
                FirstName = user.FirstName,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Username = user.Username,
                ExpiresOn = resetToken.ExpiredOn!.Value,
            };
        }

        public async Task ResetPasswordAsync(Guid token, string password, CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var hashed = TokenHasher.Hash(token);

            var resetToken = await _passwordResetTokenRepository.GetPasswordResetTokens()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenId == hashed, cancellationToken);

            // Validate before the transactional region and before anything is assigned to a
            // tracked entity — a rejected reset must leave the change tracker clean.
            _validator.ValidatePassword(password);
            _validator.ValidateResetToken(resetToken, timestamp);

            var user = resetToken.User;
            var (hash, salt) = PasswordHasher.Hash(password);

            user.Password = hash;
            user.Salt = salt;
            user.UpdatedOn = timestamp;
            user.UpdatedBy = user.UserId;

            // A reset is also how someone locked out gets back in, so it clears the
            // lock-out. Otherwise the new password would be correct and still refused.
            user.IsLocked = false;
            user.LockedUntil = null;
            user.FailedLoginCount = 0;

            resetToken.UsedOn = timestamp;
            resetToken.InvalidatedOn = timestamp;
            resetToken.InvalidatedBy = user.UserId;

            // "I forgot my password" and "someone else is in my account" arrive through the
            // same door, so changing the credential closes every session opened with the old
            // one. Anyone still holding a grant is signed out at its next rotation.
            var liveGrants = await _accessRefreshTokenRepository.GetAccessRefreshTokens()
                .Where(t => t.UserId == user.UserId && t.UsedOn == null && t.InvalidatedOn == null)
                .ToListAsync(cancellationToken);

            foreach (var grant in liveGrants)
            {
                grant.InvalidatedOn = timestamp;
                grant.InvalidatedBy = user.UserId;
            }

            var resetLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.PasswordResetCompleted,
                user.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.PasswordResetCompleted, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.UpdateUser(user);
                _passwordResetTokenRepository.UpdatePasswordResetToken(resetToken);

                foreach (var grant in liveGrants)
                {
                    _accessRefreshTokenRepository.UpdateAccessRefreshToken(grant);
                }

                _activityLogRepository.AddActivityLog(resetLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }
        }

        /// <summary>
        /// Voids any recovery link still standing for the account, issues one fresh link,
        /// writes the audit row and commits. Returns the raw Guid for the mail — the only
        /// copy that can open it, since the row holds just its hash — together with the
        /// expiry that was actually persisted, so the sentence in the mail and the row the
        /// link is checked against agree to the millisecond.
        /// </summary>
        private async Task<(Guid RawToken, DateTime ExpiresOn)> IssueResetTokenAsync(
            User user,
            Guid? actingUserId,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            // Only the newest link works. Without this, a second request would leave two
            // live links against one account and widen the window for a stale one.
            var live = await _passwordResetTokenRepository.GetPasswordResetTokens()
                .Where(t => t.UserId == user.UserId && t.UsedOn == null && t.InvalidatedOn == null)
                .ToListAsync(cancellationToken);

            foreach (var token in live)
            {
                token.InvalidatedOn = timestamp;
                token.InvalidatedBy = actingUserId;
            }

            var rawToken = Guid.NewGuid();

            var resetToken = new PasswordResetToken
            {
                // TokenId is nchar(64) and ToHexString of a SHA-256 is exactly 64
                // characters, so the fixed-length column never pads.
                TokenId = TokenHasher.Hash(rawToken),
                UserId = user.UserId,
                CreatedOn = timestamp,
                CreatedBy = actingUserId,
                ExpiredOn = timestamp.Add(ResetLifetime),
            };

            var requestedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.PasswordResetRequested,
                actingUserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.PasswordResetRequested, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                foreach (var token in live)
                {
                    _passwordResetTokenRepository.UpdatePasswordResetToken(token);
                }

                _passwordResetTokenRepository.AddPasswordResetToken(resetToken);
                _activityLogRepository.AddActivityLog(requestedLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            return (rawToken, resetToken.ExpiredOn!.Value);
        }

        private Task SendResetEmailAsync(
            User user,
            (Guid RawToken, DateTime ExpiresOn) issued,
            CancellationToken cancellationToken)
            => _emailService.SendPasswordResetEmailAsync(
                user.Email,
                user.FirstName,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Username,
                issued.RawToken,
                issued.ExpiresOn,
                cancellationToken);
    }
}
