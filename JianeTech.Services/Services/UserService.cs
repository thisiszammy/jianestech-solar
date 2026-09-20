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
    /// Account creation and lookups. This is the canonical shape every service in this
    /// project follows: primitives in, audit row paired with the write, validation before
    /// the transaction, rollback on any failure.
    /// </summary>
    public class UserService : IUserService
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserRepository _userRepository;
        private readonly IUserActivationTokenRepository _activationTokenRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly IEmailService _emailService;
        private readonly UserValidator _validator;
        private readonly IClientContextAccessor _clientContextAccessor;
        private readonly IDateTimeService _dateTime;

        /// <summary>
        /// Injected for one job: recording an invitation mail that failed after the account
        /// was already committed. The <c>[ServiceCaller]</c> lines around the Brevo call
        /// itself belong to <see cref="BrevoEmailService"/>, which is the service that
        /// actually makes it.
        /// </summary>
        private readonly ILogger<UserService> _logger;

        public UserService(
            IUnitOfWork unitOfWork,
            IUserRepository userRepository,
            IUserActivationTokenRepository activationTokenRepository,
            IActivityLogRepository activityLogRepository,
            IEmailService emailService,
            UserValidator validator,
            IClientContextAccessor clientContextAccessor,
            IDateTimeService dateTime,
            ILogger<UserService> logger)
        {
            _unitOfWork = unitOfWork;
            _userRepository = userRepository;
            _activationTokenRepository = activationTokenRepository;
            _activityLogRepository = activityLogRepository;
            _emailService = emailService;
            _validator = validator;
            _clientContextAccessor = clientContextAccessor;
            _dateTime = dateTime;
            _logger = logger;
        }

        public async Task<InvitationResultDTO> InviteUserAsync(
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            UserTypeEnum userType,
            Guid? createdBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = firstName?.Trim() ?? string.Empty,
                LastName = lastName?.Trim() ?? string.Empty,
                Username = username?.Trim() ?? string.Empty,
                Email = email?.Trim() ?? string.Empty,
                // dbo.Users.PhoneNumber is NOT NULL, so an absent number is stored as empty.
                PhoneNumber = phoneNumber?.Trim() ?? string.Empty,

                // Nobody holds this credential, so nobody can sign in as this account until
                // the invitee spends their link and chooses one.
                Password = string.Empty,
                Salt = string.Empty,

                UserType = (int)userType,

                // Active from the start, not switched off pending activation. IsActive
                // records an administrator's decision about whether an account may be used
                // at all, and an invitation is not that decision — it is the account's
                // normal first state. Keeping the two apart is what lets the register offer
                // the off switch on a Pending row and mean it, and what stops "reactivate"
                // appearing beside an account that was never activated in the first place.
                // "Waiting on an invitation" is read from the token rows instead; see
                // GetUserDTOs.
                IsActive = true,

                IsLocked = false,
                FailedLoginCount = 0,
                CreatedOn = timestamp,
                CreatedBy = createdBy,
            };

            var (hash, salt) = PasswordHasher.CreateUnusable();
            newUser.Password = hash;
            newUser.Salt = salt;

            // The raw Guid goes to the mail and nowhere else; the row keeps only its hash.
            var rawToken = Guid.NewGuid();
            var invitation = AccountActivationService.BuildInvitationToken(
                rawToken, newUser.UserId, createdBy, timestamp);

            var invitedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserInvited,
                createdBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.UserInvited, newUser.Username),
                ActivityLogReferenceTypeEnum.User,
                newUser.UserId);

            // Validate before the transactional region - a UserValidationException must
            // never leave an open transaction behind.
            await _validator.ValidateInviteUser(newUser, _userRepository.GetUsers(), cancellationToken);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.AddUser(newUser);
                _activationTokenRepository.AddUserActivationToken(invitation);
                _activityLogRepository.AddActivityLog(invitedLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            // Sent after the commit, because the link has to exist before it can be
            // clicked - and deliberately outside the transaction, because Brevo is not
            // transactional and holding a database transaction open across an HTTP call to
            // a third party is how a slow mail server becomes a lock-wait timeout.
            try
            {
                await _emailService.SendActivationEmailAsync(
                    newUser.Email,
                    newUser.FirstName,
                    $"{newUser.FirstName} {newUser.LastName}".Trim(),
                    newUser.Username,
                    rawToken,
                    invitation.ExpiredOn!.Value,
                    cancellationToken);

                return new InvitationResultDTO { UserId = newUser.UserId, EmailSent = true };
            }
            catch (Exception ex)
            {
                // Not rethrown, and the account is not rolled back. What is on the other
                // side of a failure here is a real account with a live invitation against
                // it - the recoverable state, and exactly what the register's resend button
                // is for. Rolling back would throw away a correctly created account because
                // a third party was unreachable; reporting success would hide a mail that
                // never arrived. The caller is told both facts and words them for the
                // administrator watching.
                _logger.LogError(ex,
                    "Invitation mail failed for {UserId}. The account and its invitation stand.",
                    newUser.UserId);

                return new InvitationResultDTO
                {
                    UserId = newUser.UserId,
                    EmailSent = false,
                    EmailError = ex is EmailDeliveryException
                        ? ex.Message
                        : "The invitation email could not be sent.",
                };
            }
        }

        public async Task<InvitationResultDTO> SelfRegisterUserAsync(
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            RegistrationInterestEnum interest,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = firstName?.Trim() ?? string.Empty,
                LastName = lastName?.Trim() ?? string.Empty,
                Username = username?.Trim() ?? string.Empty,
                Email = email?.Trim() ?? string.Empty,
                PhoneNumber = phoneNumber?.Trim() ?? string.Empty,

                Password = string.Empty,
                Salt = string.Empty,

                // Fixed here, never taken from the caller. This method is reached from an
                // anonymous endpoint, and a role read off an anonymous request body is a
                // way to ask the platform for an administrator account.
                UserType = (int)UserTypeEnum.User,

                // Active from the start, for the reason InviteUserAsync sets out at
                // length: IsActive records an administrator's decision about whether an
                // account may be used, and nobody has made one yet. "Waiting on its link"
                // is read from the token rows and shows as Pending.
                IsActive = true,

                IsLocked = false,
                FailedLoginCount = 0,
                CreatedOn = timestamp,

                // Null, and not an oversight. Nobody at the fund created this row, and the
                // register says so: its Added column reads "Unidentified", which is the
                // truth. Who did it is on the audit row below, where it belongs.
                CreatedBy = null,
            };

            var (hash, salt) = PasswordHasher.CreateUnusable();
            newUser.Password = hash;
            newUser.Salt = salt;

            // The raw Guid goes to the mail and nowhere else; the row keeps only its hash.
            var rawToken = Guid.NewGuid();
            var invitation = AccountActivationService.BuildInvitationToken(
                rawToken, newUser.UserId, createdBy: null, timestamp);

            // The registrant is the actor. They are not signed in — the endpoint is
            // anonymous — but the row is unambiguously theirs, which is the same reading
            // AccountActivationService.ActivateAccountAsync takes of the invitee who sets
            // their own password. The user is inserted in the same batch, so the FK on
            // ExecutedBy resolves against a row EF writes first.
            var registeredLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserSelfRegistered,
                newUser.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(
                    ActivityEnum.UserSelfRegistered, newUser.Username, interest.ToLabel()),
                ActivityLogReferenceTypeEnum.User,
                newUser.UserId);

            // Validate before the transactional region - a UserValidationException must
            // never leave an open transaction behind.
            await _validator.ValidateSelfRegistration(
                newUser, _userRepository.GetUsers(), cancellationToken);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.AddUser(newUser);
                _activationTokenRepository.AddUserActivationToken(invitation);
                _activityLogRepository.AddActivityLog(registeredLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            // Sent after the commit and outside the transaction, for the reasons set out
            // in InviteUserAsync. A failure here is reported rather than thrown: what is
            // on the other side of it is a real account with a live invitation against it,
            // and an administrator can resend from the register.
            try
            {
                await _emailService.SendActivationEmailAsync(
                    newUser.Email,
                    newUser.FirstName,
                    $"{newUser.FirstName} {newUser.LastName}".Trim(),
                    newUser.Username,
                    rawToken,
                    invitation.ExpiredOn!.Value,
                    cancellationToken);

                return new InvitationResultDTO { UserId = newUser.UserId, EmailSent = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Registration mail failed for {UserId}. The account and its invitation stand.",
                    newUser.UserId);

                return new InvitationResultDTO
                {
                    UserId = newUser.UserId,
                    EmailSent = false,
                    EmailError = ex is EmailDeliveryException
                        ? ex.Message
                        : "The email could not be sent.",
                };
            }
        }

        public async Task<Guid> RegisterUserAsync(
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            string password,
            UserTypeEnum userType,
            Guid? createdBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var (hash, salt) = PasswordHasher.Hash(password ?? string.Empty);

            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = firstName?.Trim() ?? string.Empty,
                LastName = lastName?.Trim() ?? string.Empty,
                Username = username?.Trim() ?? string.Empty,
                Email = email?.Trim() ?? string.Empty,
                // dbo.Users.PhoneNumber is NOT NULL, so an absent number is stored as empty.
                PhoneNumber = phoneNumber?.Trim() ?? string.Empty,
                Password = hash,
                Salt = salt,
                UserType = (int)userType,
                IsActive = true,
                IsLocked = false,
                FailedLoginCount = 0,
                CreatedOn = timestamp,
                CreatedBy = createdBy,
            };

            var registeredLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserRegistered,
                createdBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.UserRegistered, newUser.Username),
                ActivityLogReferenceTypeEnum.User,
                newUser.UserId);

            // Validate before the transactional region - a UserValidationException must
            // never leave an open transaction behind.
            await _validator.ValidateRegisterUser(
                newUser, password ?? string.Empty, _userRepository.GetUsers(), cancellationToken);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.AddUser(newUser);
                _activityLogRepository.AddActivityLog(registeredLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            return newUser.UserId;
        }

        public async Task UpdateUserAsync(
            Guid userId,
            string firstName,
            string lastName,
            string username,
            string email,
            string phoneNumber,
            UserTypeEnum userType,
            Guid? updatedBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var user = await _userRepository.GetUsers()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.DeletedOn == null, cancellationToken);

            // Validate before the transactional region, and before anything is assigned to
            // the tracked entity - a rejected edit must leave the change tracker clean.
            await _validator.ValidateUpdateUser(
                user,
                firstName,
                lastName,
                username,
                email,
                phoneNumber,
                _userRepository.GetUsers(),
                cancellationToken);

            user.FirstName = firstName?.Trim() ?? string.Empty;
            user.LastName = lastName?.Trim() ?? string.Empty;
            user.Username = username?.Trim() ?? string.Empty;
            user.Email = email?.Trim() ?? string.Empty;
            user.PhoneNumber = phoneNumber?.Trim() ?? string.Empty;
            user.UserType = (int)userType;
            user.UpdatedOn = timestamp;
            user.UpdatedBy = updatedBy;

            var updatedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserUpdated,
                updatedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.UserUpdated, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.UpdateUser(user);
                _activityLogRepository.AddActivityLog(updatedLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }
        }

        public async Task SetUserActiveAsync(
            Guid userId,
            bool isActive,
            Guid? updatedBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var user = await _userRepository.GetUsers()
                .FirstOrDefaultAsync(u => u.UserId == userId && u.DeletedOn == null, cancellationToken);

            _validator.ValidateSetUserActive(user, isActive, updatedBy);

            user.IsActive = isActive;
            user.UpdatedOn = timestamp;
            user.UpdatedBy = updatedBy;

            // Reactivating clears the lock-out too. An account that was locked by failed
            // passwords and then deactivated would otherwise come back still locked, with
            // no way to reach it from the console - two separate blocks, one switch.
            if (isActive && user.IsLocked)
            {
                user.IsLocked = false;
                user.LockedUntil = null;
                user.FailedLoginCount = 0;
            }

            var activity = isActive ? ActivityEnum.UserReactivated : ActivityEnum.UserDeactivated;

            var stateLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                activity,
                updatedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(activity, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.UpdateUser(user);
                _activityLogRepository.AddActivityLog(stateLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }
        }

        public async Task<QueryableEntityDto<UserDTO>> GetUserDTOs(
            int page,
            int pageSize,
            string? search,
            int? userType,
            bool? isActive,
            bool? isLocked,
            bool? isPending,
            CancellationToken cancellationToken)
        {
            // Clamped rather than rejected: paging inputs arrive from a query string, and a
            // nonsense page size should degrade to a sane one, not 500 the register.
            page = page < 1 ? 1 : page;
            pageSize = pageSize switch
            {
                < 1 => DefaultPageSize,
                > MaxPageSize => MaxPageSize,
                _ => pageSize,
            };

            var signedInActivity = (int)ActivityEnum.UserLoggedIn;

            // Soft-delete only: a retired account is never listed, which is also the set the
            // validator checks uniqueness against.
            var query = _userRepository.GetUsers().Where(u => u.DeletedOn == null);

            if (userType.HasValue)
            {
                query = query.Where(u => u.UserType == userType.Value);
            }

            if (isActive.HasValue)
            {
                query = query.Where(u => u.IsActive == isActive.Value);
            }

            if (isLocked.HasValue)
            {
                query = query.Where(u => u.IsLocked == isLocked.Value);
            }

            // Waiting on an invitation: invited at least once, and none of those links has
            // been spent. An account with no invitation history at all is not pending — it
            // was created with a password of its own, as the seeded first administrator
            // was. The same rule as AccountActivationService.HasActivated, written as a
            // predicate EF can translate rather than as a method it cannot.
            if (isPending.HasValue)
            {
                query = isPending.Value
                    ? query.Where(u =>
                        u.UserActivationTokenUsers.Any()
                        && !u.UserActivationTokenUsers.Any(t => t.UsedOn != null))
                    : query.Where(u =>
                        !u.UserActivationTokenUsers.Any()
                        || u.UserActivationTokenUsers.Any(t => t.UsedOn != null));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    u.FirstName.Contains(term)
                    || u.LastName.Contains(term)
                    || u.Username.Contains(term)
                    || u.Email.Contains(term)
                    || u.PhoneNumber.Contains(term));
            }

            // Counted off the filtered query before Skip/Take, so the page count the UI
            // derives agrees with the rows it is handed.
            var totalRows = await query.CountAsync(cancellationToken);

            // Newest first, so an account created a moment ago is row 1 of page 1 rather
            // than buried somewhere alphabetical. Username breaks ties, which also keeps the
            // order stable across pages for seeded rows that carry no CreatedOn.
            var rows = await query
                .OrderByDescending(u => u.CreatedOn)
                .ThenBy(u => u.Username)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new
                {
                    u.UserId,
                    u.FirstName,
                    u.LastName,
                    u.Username,
                    u.Email,
                    u.PhoneNumber,
                    u.UserType,
                    u.IsActive,
                    u.IsLocked,
                    u.LockedUntil,
                    u.CreatedOn,
                    IsPending = u.UserActivationTokenUsers.Any()
                        && !u.UserActivationTokenUsers.Any(t => t.UsedOn != null),
                    CreatorFirstName = u.CreatedByNavigation != null ? u.CreatedByNavigation.FirstName : null,
                    CreatorLastName = u.CreatedByNavigation != null ? u.CreatedByNavigation.LastName : null,
                    LastSignInOn = u.ActivityLogs
                        .Where(l => l.Activity == signedInActivity)
                        .OrderByDescending(l => l.ExecutedOn)
                        .Select(l => (DateTime?)l.ExecutedOn)
                        .FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);

            // ToLabel() is an extension method and the status ladder is a C# switch, so
            // neither survives translation to SQL - the query above stops at an anonymous
            // type and the display shape is assembled here.
            var items = rows.Select(r =>
            {
                var (status, tone) = ResolveStatus(r.IsActive, r.IsPending, r.IsLocked);

                return new UserDTO
                {
                    UserId = r.UserId,
                    FirstName = r.FirstName,
                    LastName = r.LastName,
                    Username = r.Username,
                    Email = r.Email,
                    PhoneNumber = r.PhoneNumber,
                    UserType = r.UserType,
                    UserTypeLabel = ((UserTypeEnum)r.UserType).ToLabel(),
                    IsActive = r.IsActive,
                    IsLocked = r.IsLocked,
                    IsPending = r.IsPending,
                    LockedUntil = r.LockedUntil,
                    Status = status,
                    StatusTone = tone,
                    LastSignInOn = r.LastSignInOn,
                    CreatedOn = r.CreatedOn,
                    CreatedByName = DashboardService.ComposeName(r.CreatorFirstName, r.CreatorLastName),
                };
            }).ToList();

            return new QueryableEntityDto<UserDTO>
            {
                Items = items,
                TotalRows = totalRows,
            };
        }

        public List<UserTypeOptionDTO> GetUserTypeOptionDTOs()
            => Enum.GetValues<UserTypeEnum>()
                .Select(userType => new UserTypeOptionDTO
                {
                    UserType = (int)userType,
                    Label = userType.ToLabel(),
                })
                .ToList();

        public Task<bool> AnyUsersExistAsync(CancellationToken cancellationToken)
            => _userRepository.GetUsers().AnyAsync(u => u.DeletedOn == null, cancellationToken);

        /// <summary>
        /// Deactivation outranks everything else, and the order matters. A lock is
        /// automatic and temporary — five wrong passwords set it — while being inactive is
        /// a decision somebody made. Reporting the lock first meant that deactivating an
        /// account that happened to be locked changed nothing in the Status column, so the
        /// person who had just done it saw a success message beside a row that had not moved.
        ///
        /// Pending sits next, above Locked, for the same reason: an invitation that has not
        /// been accepted is the larger fact about the account, and a lock earned by someone
        /// else guessing at the username is noise beside it — activation clears that lock
        /// anyway.
        ///
        /// The four read as a partition, and the register's status filter partitions the
        /// same way, so what you filter for is what the column says.
        /// </summary>
        private static (string Status, string Tone) ResolveStatus(bool isActive, bool isPending, bool isLocked)
            => !isActive ? ("Inactive", "warn")
             : isPending ? ("Pending", "pending")
             : isLocked ? ("Locked", "danger")
             : ("Active", "success");
    }
}
