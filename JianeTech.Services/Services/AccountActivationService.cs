using JianeTech.Data.Entities;
using JianeTech.Data.Enums;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using JianeTech.Services.DTOs;
using JianeTech.Services.Interfaces;
using JianeTech.Services.Security;
using JianeTech.Services.Validators;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// The life of an invitation link after it has been issued.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The link carries a raw Guid; <c>dbo.UserActivationTokens.TokenId</c> holds only the
    /// SHA-256 of it, exactly as <c>dbo.AccessRefreshTokens</c> does for refresh grants. A
    /// leaked row therefore cannot be replayed as a link, and the lookup stays an exact
    /// match on a fixed-width key.
    /// </para>
    /// <para>
    /// The raw Guid exists in memory for the length of one call and goes to exactly one
    /// place: <see cref="IEmailService"/>. It is never returned, never logged, and never
    /// reaches a DTO.
    /// </para>
    /// </remarks>
    public class AccountActivationService : IAccountActivationService
    {
        /// <summary>
        /// How long an invitation stands. Long enough to survive a weekend and an inbox
        /// someone only reads on Mondays, short enough that a forwarded mail found a year
        /// later opens nothing.
        /// </summary>
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserRepository _userRepository;
        private readonly IUserActivationTokenRepository _activationTokenRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly IEmailService _emailService;
        private readonly AccountActivationValidator _validator;
        private readonly IClientContextAccessor _clientContextAccessor;
        private readonly IDateTimeService _dateTime;

        public AccountActivationService(
            IUnitOfWork unitOfWork,
            IUserRepository userRepository,
            IUserActivationTokenRepository activationTokenRepository,
            IActivityLogRepository activityLogRepository,
            IEmailService emailService,
            AccountActivationValidator validator,
            IClientContextAccessor clientContextAccessor,
            IDateTimeService dateTime)
        {
            _unitOfWork = unitOfWork;
            _userRepository = userRepository;
            _activationTokenRepository = activationTokenRepository;
            _activityLogRepository = activityLogRepository;
            _emailService = emailService;
            _validator = validator;
            _clientContextAccessor = clientContextAccessor;
            _dateTime = dateTime;
        }

        public async Task<InvitationDTO> GetInvitationDTOByToken(Guid token, CancellationToken cancellationToken)
        {
            var hashed = TokenHasher.Hash(token);

            // AsNoTracking: this is the page-load read, and nothing it returns is written
            // back. Spending the link is ActivateAccountAsync's job and loads its own copy.
            var activationToken = await _activationTokenRepository.GetUserActivationTokens()
                .AsNoTracking()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenId == hashed, cancellationToken);

            // One method is the only place the rules for a live link are written down, so
            // the page and the submit cannot drift apart on what "still valid" means.
            _validator.ValidateActivationToken(activationToken, _dateTime.Now());

            var user = activationToken.User;

            return new InvitationDTO
            {
                FirstName = user.FirstName,
                FullName = $"{user.FirstName} {user.LastName}".Trim(),
                Username = user.Username,
                ExpiresOn = activationToken.ExpiredOn!.Value,
            };
        }

        public async Task ActivateAccountAsync(Guid token, string password, CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var hashed = TokenHasher.Hash(token);

            var activationToken = await _activationTokenRepository.GetUserActivationTokens()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenId == hashed, cancellationToken);

            // Validate before the transactional region, and before anything is assigned to
            // the tracked entities — a rejected activation must leave the change tracker clean.
            _validator.ValidatePassword(password);
            _validator.ValidateActivationToken(activationToken, timestamp);

            var user = activationToken.User;
            var (hash, salt) = PasswordHasher.Hash(password);

            user.Password = hash;
            user.Salt = salt;
            user.UpdatedOn = timestamp;

            // The invitee is the actor here, not an administrator. They are not signed in —
            // this endpoint is anonymous — but the row is unambiguously theirs.
            user.UpdatedBy = user.UserId;

            // An account nobody could sign in to can still have been locked by someone
            // guessing at its username. Clearing it here means the first password ever set
            // is usable immediately, rather than meeting a lock-out earned by a stranger.
            user.IsLocked = false;
            user.LockedUntil = null;
            user.FailedLoginCount = 0;

            activationToken.UsedOn = timestamp;
            activationToken.InvalidatedOn = timestamp;
            activationToken.InvalidatedBy = user.UserId;

            var activatedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserActivated,
                user.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.UserActivated, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.UpdateUser(user);
                _activationTokenRepository.UpdateUserActivationToken(activationToken);
                _activityLogRepository.AddActivityLog(activatedLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }
        }

        public async Task ResendInvitationAsync(
            Guid userId,
            Guid? actingUserId,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var user = await _userRepository.GetUsers()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.DeletedOn == null, cancellationToken);

            var outstanding = await _activationTokenRepository.GetUserActivationTokens()
                .Where(t => t.UserId == userId)
                .ToListAsync(cancellationToken);

            _validator.ValidateResendInvitation(user, HasActivated(outstanding));

            // Every link still standing is voided before a new one is issued, so the mail
            // just sent is the only one that opens — and a link someone forwarded from an
            // older mail stops working the moment a newer invitation goes out.
            var live = outstanding
                .Where(t => t.UsedOn is null && t.InvalidatedOn is null)
                .ToList();

            foreach (var token in live)
            {
                token.InvalidatedOn = timestamp;
                token.InvalidatedBy = actingUserId;
            }

            var rawToken = Guid.NewGuid();
            var invitation = BuildInvitationToken(rawToken, user.UserId, actingUserId, timestamp);

            var resentLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.InvitationResent,
                actingUserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.InvitationResent, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                foreach (var token in live)
                {
                    _activationTokenRepository.UpdateUserActivationToken(token);
                }

                _activationTokenRepository.AddUserActivationToken(invitation);
                _activityLogRepository.AddActivityLog(resentLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            // Sent after the commit, because the link has to exist before it can be
            // clicked. A failure here is left to reach the caller: sending the mail is the
            // whole point of this method, and the administrator who pressed the button is
            // watching — unlike InviteUserAsync, where an account was also created and is
            // worth keeping. The audit row is worded as issuing a link rather than sending
            // one, so it stays true either way.
            await _emailService.SendActivationEmailAsync(
                user.Email,
                user.FirstName,
                $"{user.FirstName} {user.LastName}".Trim(),
                user.Username,
                rawToken,
                invitation.ExpiredOn!.Value,
                cancellationToken);
        }

        /// <summary>
        /// One invitation row, built the one way. Shared with <c>UserService</c>, which
        /// issues the first link at the moment the account is created.
        /// </summary>
        public static UserActivationToken BuildInvitationToken(
            Guid rawToken,
            Guid userId,
            Guid? createdBy,
            DateTime timestamp)
            => new()
            {
                // Only the hash is stored. TokenId is nchar(64) and ToHexString of a
                // SHA-256 is exactly 64 characters, so the fixed-length column never pads.
                TokenId = TokenHasher.Hash(rawToken),
                UserId = userId,
                CreatedOn = timestamp,
                CreatedBy = createdBy,
                ExpiredOn = timestamp.Add(InvitationLifetime),
            };

        /// <summary>
        /// Whether the account has a password of its own making.
        /// </summary>
        /// <remarks>
        /// An account with no invitation history at all counts as activated: it was created
        /// directly with a password — the seeded first administrator is the standing example
        /// — and has nothing to activate. Only an account that was invited and has not yet
        /// spent any of its links is still waiting.
        /// </remarks>
        public static bool HasActivated(IReadOnlyCollection<UserActivationToken> tokens)
            => tokens.Count == 0 || tokens.Any(t => t.UsedOn is not null);
    }
}
