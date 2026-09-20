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
using Microsoft.Extensions.Configuration;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// Owns the session lifecycle: credential verification, refresh-grant rotation and
    /// sign-out. Every path through this class that touches the database also writes the
    /// matching <see cref="ActivityLog"/> rows, so the audit trail cannot drift from what
    /// actually happened.
    /// </summary>
    public class AuthenticationService : IAuthenticationService
    {
        /// <summary>Failed attempts tolerated before the account locks.</summary>
        private const int MaxFailedAttempts = 5;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserRepository _userRepository;
        private readonly IAccessRefreshTokenRepository _accessRefreshTokenRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly AuthenticationValidator _validator;
        private readonly IJwtTokenGenerator _jwtTokenGenerator;
        private readonly IClientContextAccessor _clientContextAccessor;
        private readonly IDateTimeService _dateTime;

        private readonly TimeSpan _accessTokenLifetime;
        private readonly TimeSpan _shortLivedRefreshLifetime;
        private readonly TimeSpan _longLivedRefreshLifetime;

        public AuthenticationService(
            IUnitOfWork unitOfWork,
            IUserRepository userRepository,
            IAccessRefreshTokenRepository accessRefreshTokenRepository,
            IActivityLogRepository activityLogRepository,
            AuthenticationValidator validator,
            IJwtTokenGenerator jwtTokenGenerator,
            IClientContextAccessor clientContextAccessor,
            IDateTimeService dateTime,
            IConfiguration configuration)
        {
            _unitOfWork = unitOfWork;
            _userRepository = userRepository;
            _accessRefreshTokenRepository = accessRefreshTokenRepository;
            _activityLogRepository = activityLogRepository;
            _validator = validator;
            _jwtTokenGenerator = jwtTokenGenerator;
            _clientContextAccessor = clientContextAccessor;
            _dateTime = dateTime;

            _accessTokenLifetime = TimeSpan.FromMinutes(RequiredInt(configuration, "Jwt:ExpiryMinutes"));
            _shortLivedRefreshLifetime = TimeSpan.FromMinutes(RequiredInt(configuration, "Jwt:RefreshExpiryMinutes"));
            _longLivedRefreshLifetime = TimeSpan.FromDays(RequiredInt(configuration, "Jwt:LongLivedRefreshExpiryDays"));
        }

        public async Task<AuthenticationDTO> AuthenticateUserAsync(
            string username,
            string password,
            bool rememberMe,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                // No lookup happened, so there is no user to attribute this to. The row is
                // still written: a burst of actor-less failures from one IP is exactly the
                // pattern an audit trail exists to surface.
                await WriteFailedSignInAsync(batchId, ++executionOrder, null, username, timestamp, cancellationToken);
                throw new AuthenticationValidationException(AuthenticationValidator.GenericCredentialError);
            }

            var user = await _userRepository.GetUsers()
                .FirstOrDefaultAsync(u => u.Username == username && u.DeletedOn == null, cancellationToken);

            try
            {
                _validator.ValidateSignInCandidate(user);
            }
            catch (AuthenticationValidationException)
            {
                await WriteFailedSignInAsync(
                    batchId, ++executionOrder, user?.UserId, username, timestamp, cancellationToken);
                throw;
            }

            if (!PasswordHasher.Verify(password, user.Password, user.Salt))
            {
                await RecordFailedPasswordAttemptAsync(
                    batchId, ++executionOrder, user, timestamp, cancellationToken);

                if (user.IsLocked)
                    throw new AccountLockedException(AuthenticationValidator.AccountLockedError);

                throw new AuthenticationValidationException(
                    AuthenticationValidator.GenericCredentialError,
                    MaxFailedAttempts - user.FailedLoginCount);
            }

            var accessTokenExpiresOn = timestamp.Add(_accessTokenLifetime);
            var refreshTokenType = rememberMe ? TokenTypeEnum.LongLived : TokenTypeEnum.ShortLived;
            var refreshTokenExpiresOn = timestamp.Add(
                rememberMe ? _longLivedRefreshLifetime : _shortLivedRefreshLifetime);

            // Signing in supersedes any grant still open for this account, so a stale tab
            // cannot keep rotating a session the user believes they replaced.
            var supersededTokens = await _accessRefreshTokenRepository.GetAccessRefreshTokens()
                .Where(t => t.UserId == user.UserId && t.UsedOn == null && t.InvalidatedOn == null)
                .ToListAsync(cancellationToken);

            foreach (var superseded in supersededTokens)
            {
                superseded.UsedOn = timestamp;
            }

            // The raw id goes to the cookie; only its hash is ever persisted.
            var rawRefreshTokenId = Guid.NewGuid();
            var newToken = new AccessRefreshToken
            {
                TokenId = TokenHasher.Hash(rawRefreshTokenId),
                UserId = user.UserId,
                TokenType = (int)refreshTokenType,
                CreatedOn = timestamp,
                CreatedBy = user.UserId,
                ExpiredOn = refreshTokenExpiresOn,
            };

            var resetFailedCount = user.FailedLoginCount > 0;
            if (resetFailedCount)
            {
                user.FailedLoginCount = 0;
            }

            var accessToken = _jwtTokenGenerator.GenerateAccessToken(user, timestamp, accessTokenExpiresOn);

            var signedInLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserLoggedIn,
                user.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.UserLoggedIn, user.Username),
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                foreach (var superseded in supersededTokens)
                {
                    _accessRefreshTokenRepository.UpdateAccessRefreshToken(superseded);
                }

                _accessRefreshTokenRepository.AddAccessRefreshToken(newToken);

                if (resetFailedCount)
                {
                    _userRepository.UpdateUser(user);
                }

                _activityLogRepository.AddActivityLog(signedInLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            return new AuthenticationDTO
            {
                AccessToken = accessToken,
                AccessTokenExpiresOn = accessTokenExpiresOn,
                RefreshTokenId = rawRefreshTokenId,
                RefreshTokenExpiresOn = refreshTokenExpiresOn,
                RefreshTokenType = refreshTokenType,
                User = ToAuthenticatedUserDTO(user, timestamp),
            };
        }

        public async Task<AuthenticationDTO> RefreshTokenAsync(
            Guid refreshTokenId,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();
            var hashedTokenId = TokenHasher.Hash(refreshTokenId);

            var token = await _accessRefreshTokenRepository.GetAccessRefreshTokens()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenId == hashedTokenId, cancellationToken);

            try
            {
                _validator.ValidateRefreshToken(token, timestamp);
            }
            catch (RefreshTokenReplayException replay)
            {
                // A consumed grant came back. Treat it as a stolen cookie: close every live
                // session for the account before the exception reaches the caller.
                await RevokeAllLiveGrantsAsync(
                    batchId, ++executionOrder, replay.UserId, timestamp, cancellationToken);
                throw;
            }

            var accessTokenExpiresOn = timestamp.Add(_accessTokenLifetime);
            var accessToken = _jwtTokenGenerator.GenerateAccessToken(token.User, timestamp, accessTokenExpiresOn);

            // A long-lived ("remember me") grant is reused until it expires, so this path
            // mints an access token without touching the database - and therefore writes no
            // activity row, since there is no write to pair one with.
            if ((TokenTypeEnum)token.TokenType == TokenTypeEnum.LongLived)
            {
                return new AuthenticationDTO
                {
                    AccessToken = accessToken,
                    AccessTokenExpiresOn = accessTokenExpiresOn,
                    RefreshTokenId = refreshTokenId,
                    RefreshTokenExpiresOn = token.ExpiredOn!.Value,
                    RefreshTokenType = TokenTypeEnum.LongLived,
                    User = ToAuthenticatedUserDTO(token.User, null),
                };
            }

            var refreshTokenExpiresOn = timestamp.Add(_shortLivedRefreshLifetime);
            token.UsedOn = timestamp;

            var rawRefreshTokenId = Guid.NewGuid();
            var rotatedToken = new AccessRefreshToken
            {
                TokenId = TokenHasher.Hash(rawRefreshTokenId),
                UserId = token.UserId,
                TokenType = (int)TokenTypeEnum.ShortLived,
                CreatedOn = timestamp,
                CreatedBy = token.UserId,
                ExpiredOn = refreshTokenExpiresOn,
            };

            var refreshedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.AccessTokenRefreshed,
                token.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.AccessTokenRefreshed, token.User.Username),
                ActivityLogReferenceTypeEnum.AccessRefreshToken,
                token.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _accessRefreshTokenRepository.UpdateAccessRefreshToken(token);
                _accessRefreshTokenRepository.AddAccessRefreshToken(rotatedToken);
                _activityLogRepository.AddActivityLog(refreshedLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            return new AuthenticationDTO
            {
                AccessToken = accessToken,
                AccessTokenExpiresOn = accessTokenExpiresOn,
                RefreshTokenId = rawRefreshTokenId,
                RefreshTokenExpiresOn = refreshTokenExpiresOn,
                RefreshTokenType = TokenTypeEnum.ShortLived,
                User = ToAuthenticatedUserDTO(token.User, null),
            };
        }

        public async Task LogoutAsync(
            Guid refreshTokenId,
            Guid? actingUserId,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();
            var hashedTokenId = TokenHasher.Hash(refreshTokenId);

            var token = await _accessRefreshTokenRepository.GetAccessRefreshTokens()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TokenId == hashedTokenId, cancellationToken);

            // Signing out is idempotent and never reports on a session the caller does not
            // own: an unknown, already-closed, or foreign grant is a silent no-op.
            if (token is null || token.UsedOn is not null || token.InvalidatedOn is not null)
                return;

            if (actingUserId is null || token.UserId != actingUserId.Value)
                return;

            token.InvalidatedOn = timestamp;
            token.InvalidatedBy = token.UserId;

            var signedOutLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.UserLoggedOut,
                token.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.UserLoggedOut, token.User.Username),
                ActivityLogReferenceTypeEnum.User,
                token.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _accessRefreshTokenRepository.UpdateAccessRefreshToken(token);
                _activityLogRepository.AddActivityLog(signedOutLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }
        }

        public async Task<AuthenticatedUserDTO?> GetAuthenticatedUserDTOById(
            Guid userId,
            CancellationToken cancellationToken)
        {
            var signedInActivity = (int)ActivityEnum.UserLoggedIn;

            return await _userRepository.GetUsers()
                .Where(u => u.UserId == userId && u.DeletedOn == null)
                .Select(u => new AuthenticatedUserDTO
                {
                    UserId = u.UserId,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Username = u.Username,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    UserType = u.UserType,
                    UserTypeLabel = ((UserTypeEnum)u.UserType).ToLabel(),
                    LastSignInOn = u.ActivityLogs
                        .Where(l => l.Activity == signedInActivity)
                        .OrderByDescending(l => l.ExecutedOn)
                        .Select(l => (DateTime?)l.ExecutedOn)
                        .FirstOrDefault(),
                })
                .FirstOrDefaultAsync(cancellationToken);
        }

        /// <summary>
        /// Bumps the failure counter, locking the account once the threshold is reached, and
        /// records the attempt. Runs in its own transaction: it must persist even though the
        /// caller is about to throw.
        /// </summary>
        private async Task RecordFailedPasswordAttemptAsync(
            Guid batchId,
            int executionOrder,
            User user,
            DateTime timestamp,
            CancellationToken cancellationToken)
        {
            user.FailedLoginCount++;
            var thresholdReached = user.FailedLoginCount >= MaxFailedAttempts;
            if (thresholdReached)
            {
                user.IsLocked = true;
                user.LockedUntil = null;
            }

            var activity = thresholdReached ? ActivityEnum.AccountLocked : ActivityEnum.LoginFailed;
            var detail = thresholdReached
                ? ActivityLog.BuildDetail(activity, user.Username, user.FailedLoginCount)
                : ActivityLog.BuildDetail(activity, user.Username);

            var log = ActivityLog.Create(
                batchId,
                executionOrder,
                activity,
                user.UserId,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                detail,
                ActivityLogReferenceTypeEnum.User,
                user.UserId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _userRepository.UpdateUser(user);
                _activityLogRepository.AddActivityLog(log);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }
        }

        /// <summary>Audits a sign-in that never reached the password check.</summary>
        private async Task WriteFailedSignInAsync(
            Guid batchId,
            int executionOrder,
            Guid? executedBy,
            string attemptedUsername,
            DateTime timestamp,
            CancellationToken cancellationToken)
        {
            var log = ActivityLog.Create(
                batchId,
                executionOrder,
                ActivityEnum.LoginFailed,
                executedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(
                    ActivityEnum.LoginFailed,
                    string.IsNullOrWhiteSpace(attemptedUsername) ? "(blank)" : attemptedUsername),
                executedBy.HasValue ? ActivityLogReferenceTypeEnum.User : null,
                executedBy);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _activityLogRepository.AddActivityLog(log);

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
        /// Closes every live grant for a user after a replay. Failures here are swallowed:
        /// the caller is already throwing <c>RefreshTokenReplayException</c>, and losing
        /// that signal to a secondary database error would be strictly worse.
        /// </summary>
        private async Task RevokeAllLiveGrantsAsync(
            Guid batchId,
            int executionOrder,
            Guid userId,
            DateTime timestamp,
            CancellationToken cancellationToken)
        {
            try
            {
                var liveGrants = await _accessRefreshTokenRepository.GetAccessRefreshTokens()
                    .Where(t => t.UserId == userId && t.UsedOn == null && t.InvalidatedOn == null)
                    .ToListAsync(cancellationToken);

                var username = await _userRepository.GetUsers()
                    .Where(u => u.UserId == userId)
                    .Select(u => u.Username)
                    .FirstOrDefaultAsync(cancellationToken) ?? userId.ToString();

                foreach (var grant in liveGrants)
                {
                    grant.InvalidatedOn = timestamp;
                    grant.InvalidatedBy = userId;
                }

                var log = ActivityLog.Create(
                    batchId,
                    executionOrder,
                    ActivityEnum.RefreshTokenReplayDetected,
                    userId,
                    timestamp,
                    _clientContextAccessor.IpAddress,
                    _clientContextAccessor.UserAgent,
                    ActivityLog.BuildDetail(ActivityEnum.RefreshTokenReplayDetected, username),
                    ActivityLogReferenceTypeEnum.User,
                    userId);

                try
                {
                    await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                    foreach (var grant in liveGrants)
                    {
                        _accessRefreshTokenRepository.UpdateAccessRefreshToken(grant);
                    }

                    _activityLogRepository.AddActivityLog(log);

                    await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
                }
                catch
                {
                    try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                    catch { }
                    throw;
                }
            }
            catch
            {
                // Intentionally swallowed - see the summary above.
            }
        }

        private static AuthenticatedUserDTO ToAuthenticatedUserDTO(User user, DateTime? signedInOn)
            => new()
            {
                UserId = user.UserId,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Username = user.Username,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                UserType = user.UserType,
                UserTypeLabel = ((UserTypeEnum)user.UserType).ToLabel(),
                LastSignInOn = signedInOn,
            };

        private static int RequiredInt(IConfiguration configuration, string key)
        {
            var raw = configuration[key]
                ?? throw new InvalidOperationException(
                    $"{key} is not configured. Set {key.Replace(":", "__")} in .env or appsettings.json.");

            return int.TryParse(raw, out var value) && value > 0
                ? value
                : throw new InvalidOperationException($"{key} must be a positive integer (got '{raw}').");
        }
    }
}
