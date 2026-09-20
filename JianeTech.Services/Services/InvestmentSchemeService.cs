using JianeTech.Data.Entities;
using JianeTech.Data.Enums;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using JianeTech.Services.DTOs;
using JianeTech.Services.Interfaces;
using JianeTech.Services.Validators;
using Microsoft.EntityFrameworkCore;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// The terms an investor can place capital on: the floor under a placement, the profit
    /// interest it earns, how long it runs, how much of the head of that term pays nothing,
    /// and what the installation the investor receives is worth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here computes a periodic return, a per-annum profit or a payout schedule.
    /// Those are pure functions of the six stored terms, and the configuration page
    /// recomputes them on every keystroke — see the note on <see cref="InvestmentSchemeDTO"/>
    /// for why they are not sent over the wire as well. The one derived fact this service
    /// does enforce is the one that makes a scheme coherent at all, and it lives in the
    /// validator: the grace period must leave some of the term to pay out over.
    /// </para>
    /// <para>
    /// Unlike <c>InstallmentScheme</c>, this entity carries no <c>CreatedByNavigation</c>
    /// — <c>dbo.InvestmentSchemes</c> has no foreign key to <c>dbo.Users</c> — so the two
    /// actor names are read with correlated sub-selects against the user register instead.
    /// That is a join EF Core translates perfectly well; what it must not become is a
    /// second round trip per row.
    /// </para>
    /// </remarks>
    public class InvestmentSchemeService : IInvestmentSchemeService
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IInvestmentSchemeRepository _schemeRepository;
        private readonly IUserRepository _userRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly InvestmentSchemeValidator _validator;
        private readonly IClientContextAccessor _clientContextAccessor;
        private readonly IDateTimeService _dateTime;

        public InvestmentSchemeService(
            IUnitOfWork unitOfWork,
            IInvestmentSchemeRepository schemeRepository,
            IUserRepository userRepository,
            IActivityLogRepository activityLogRepository,
            InvestmentSchemeValidator validator,
            IClientContextAccessor clientContextAccessor,
            IDateTimeService dateTime)
        {
            _unitOfWork = unitOfWork;
            _schemeRepository = schemeRepository;
            _userRepository = userRepository;
            _activityLogRepository = activityLogRepository;
            _validator = validator;
            _clientContextAccessor = clientContextAccessor;
            _dateTime = dateTime;
        }

        public async Task<Guid> CreateInvestmentSchemeAsync(
            string description,
            SchemeTypeEnum schemeType,
            decimal minimumInvestment,
            decimal incomeInterest,
            int gracePeriodMonths,
            int investmentYears,
            decimal freeInstallationAmt,
            Guid? createdBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var newScheme = new InvestmentScheme
            {
                // dbo.InvestmentSchemes.SchemeId is a uniqueidentifier configured
                // ValueGeneratedNever, so the key is minted here. No contention to worry
                // about, unlike the installment register's int key.
                SchemeId = Guid.NewGuid(),
                Description = description?.Trim() ?? string.Empty,
                SchemeType = (int)schemeType,
                MinimumInvestment = minimumInvestment,
                IncomeInterest = incomeInterest,
                GracePeriodMonths = gracePeriodMonths,
                InvestmentYears = investmentYears,
                FreeInstallationAmt = freeInstallationAmt,

                // On offer from the moment it is saved. A scheme nobody can be offered is
                // what the register's off switch is for, not what a new row should be.
                IsActive = true,

                CreatedOn = timestamp,
                CreatedBy = createdBy,
            };

            var createdLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.InvestmentSchemeCreated,
                createdBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.InvestmentSchemeCreated, newScheme.Description),
                ActivityLogReferenceTypeEnum.InvestmentScheme,
                newScheme.SchemeId);

            // Validate before the transactional region - an InvestmentSchemeValidationException
            // must never leave an open transaction behind.
            await _validator.ValidateCreateInvestmentScheme(
                newScheme, _schemeRepository.GetInvestmentSchemes(), cancellationToken);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _schemeRepository.AddInvestmentScheme(newScheme);
                _activityLogRepository.AddActivityLog(createdLog);

                await _unitOfWork.CommitAsync<DatabaseContext>(cancellationToken);
            }
            catch
            {
                try { await _unitOfWork.RollbackAsync<DatabaseContext>(cancellationToken); }
                catch { }
                throw;
            }

            return newScheme.SchemeId;
        }

        public async Task UpdateInvestmentSchemeAsync(
            Guid schemeId,
            string description,
            SchemeTypeEnum schemeType,
            decimal minimumInvestment,
            decimal incomeInterest,
            int gracePeriodMonths,
            int investmentYears,
            decimal freeInstallationAmt,
            Guid? updatedBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var scheme = await _schemeRepository.GetInvestmentSchemes()
                .FirstOrDefaultAsync(s => s.SchemeId == schemeId, cancellationToken);

            // The proposed terms are validated as a detached scheme rather than by
            // assigning them first: a rejected edit must leave the change tracker holding
            // nothing, or the next write in this request would carry it along.
            var proposed = new InvestmentScheme
            {
                SchemeId = schemeId,
                Description = description?.Trim() ?? string.Empty,
                SchemeType = (int)schemeType,
                MinimumInvestment = minimumInvestment,
                IncomeInterest = incomeInterest,
                GracePeriodMonths = gracePeriodMonths,
                InvestmentYears = investmentYears,
                FreeInstallationAmt = freeInstallationAmt,
            };

            await _validator.ValidateUpdateInvestmentScheme(
                scheme, proposed, _schemeRepository.GetInvestmentSchemes(), cancellationToken);

            scheme.Description = proposed.Description;
            scheme.SchemeType = proposed.SchemeType;
            scheme.MinimumInvestment = proposed.MinimumInvestment;
            scheme.IncomeInterest = proposed.IncomeInterest;
            scheme.GracePeriodMonths = proposed.GracePeriodMonths;
            scheme.InvestmentYears = proposed.InvestmentYears;
            scheme.FreeInstallationAmt = proposed.FreeInstallationAmt;
            scheme.UpdatedOn = timestamp;
            scheme.UpdatedBy = updatedBy;

            var updatedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.InvestmentSchemeUpdated,
                updatedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.InvestmentSchemeUpdated, scheme.Description),
                ActivityLogReferenceTypeEnum.InvestmentScheme,
                scheme.SchemeId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _schemeRepository.UpdateInvestmentScheme(scheme);
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

        public async Task SetInvestmentSchemeActiveAsync(
            Guid schemeId,
            bool isActive,
            Guid? updatedBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var scheme = await _schemeRepository.GetInvestmentSchemes()
                .FirstOrDefaultAsync(s => s.SchemeId == schemeId, cancellationToken);

            await _validator.ValidateSetInvestmentSchemeActive(
                scheme, isActive, _schemeRepository.GetInvestmentSchemes(), cancellationToken);

            scheme.IsActive = isActive;
            scheme.UpdatedOn = timestamp;
            scheme.UpdatedBy = updatedBy;

            var activity = isActive
                ? ActivityEnum.InvestmentSchemeReactivated
                : ActivityEnum.InvestmentSchemeDeactivated;

            var stateLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                activity,
                updatedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(activity, scheme.Description),
                ActivityLogReferenceTypeEnum.InvestmentScheme,
                scheme.SchemeId);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _schemeRepository.UpdateInvestmentScheme(scheme);
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

        public async Task<QueryableEntityDto<InvestmentSchemeDTO>> GetInvestmentSchemeDTOs(
            int page,
            int pageSize,
            string? search,
            bool? isActive,
            SchemeTypeEnum? schemeType,
            int? minInvestmentYears,
            int? maxInvestmentYears,
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

            var query = _schemeRepository.GetInvestmentSchemes().AsQueryable();
            var users = _userRepository.GetUsers();

            if (isActive.HasValue)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }

            if (schemeType.HasValue)
            {
                var wanted = (int)schemeType.Value;
                query = query.Where(s => s.SchemeType == wanted);
            }

            // A band rather than an exact term, the same way the installment register
            // filters: nobody looks for a scheme by remembering it ran seven years.
            if (minInvestmentYears.HasValue)
            {
                var min = minInvestmentYears.Value;
                query = query.Where(s => s.InvestmentYears >= min);
            }

            if (maxInvestmentYears.HasValue)
            {
                var max = maxInvestmentYears.Value;
                query = query.Where(s => s.InvestmentYears <= max);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(s => s.Description.Contains(term));
            }

            // Counted off the filtered query before Skip/Take, so the page count the UI
            // derives agrees with the rows it is handed.
            var totalRows = await query.CountAsync(cancellationToken);

            // Newest first, as every register in this console is. Description breaks ties
            // and keeps the order stable across pages for rows that carry no CreatedOn —
            // a Guid key cannot stand in for arrival order the way an int one can.
            var rows = await query
                .OrderByDescending(s => s.CreatedOn)
                .ThenBy(s => s.Description)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.SchemeId,
                    s.Description,
                    s.SchemeType,
                    s.MinimumInvestment,
                    s.IncomeInterest,
                    s.GracePeriodMonths,
                    s.InvestmentYears,
                    s.FreeInstallationAmt,
                    s.IsActive,
                    s.CreatedOn,
                    s.UpdatedOn,
                    CreatorFirstName = users.Where(u => u.UserId == s.CreatedBy).Select(u => u.FirstName).FirstOrDefault(),
                    CreatorLastName = users.Where(u => u.UserId == s.CreatedBy).Select(u => u.LastName).FirstOrDefault(),
                    EditorFirstName = users.Where(u => u.UserId == s.UpdatedBy).Select(u => u.FirstName).FirstOrDefault(),
                    EditorLastName = users.Where(u => u.UserId == s.UpdatedBy).Select(u => u.LastName).FirstOrDefault(),
                })
                .ToListAsync(cancellationToken);

            // ComposeName, the status ladder and the scheme-type wording are all C#, so
            // none of them survives translation to SQL - the query above stops at an
            // anonymous type and the display shape is assembled here.
            var items = rows.Select(r => Compose(
                r.SchemeId, r.Description, r.SchemeType, r.MinimumInvestment, r.IncomeInterest,
                r.GracePeriodMonths, r.InvestmentYears, r.FreeInstallationAmt, r.IsActive,
                r.CreatedOn, r.UpdatedOn, r.CreatorFirstName, r.CreatorLastName,
                r.EditorFirstName, r.EditorLastName))
                .ToList();

            return new QueryableEntityDto<InvestmentSchemeDTO>
            {
                Items = items,
                TotalRows = totalRows,
            };
        }

        public async Task<InvestmentSchemeDTO?> GetInvestmentSchemeDTOById(
            Guid schemeId,
            CancellationToken cancellationToken)
        {
            var users = _userRepository.GetUsers();

            var row = await _schemeRepository.GetInvestmentSchemes()
                .Where(s => s.SchemeId == schemeId)
                .Select(s => new
                {
                    s.SchemeId,
                    s.Description,
                    s.SchemeType,
                    s.MinimumInvestment,
                    s.IncomeInterest,
                    s.GracePeriodMonths,
                    s.InvestmentYears,
                    s.FreeInstallationAmt,
                    s.IsActive,
                    s.CreatedOn,
                    s.UpdatedOn,
                    CreatorFirstName = users.Where(u => u.UserId == s.CreatedBy).Select(u => u.FirstName).FirstOrDefault(),
                    CreatorLastName = users.Where(u => u.UserId == s.CreatedBy).Select(u => u.LastName).FirstOrDefault(),
                    EditorFirstName = users.Where(u => u.UserId == s.UpdatedBy).Select(u => u.FirstName).FirstOrDefault(),
                    EditorLastName = users.Where(u => u.UserId == s.UpdatedBy).Select(u => u.LastName).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(cancellationToken);

            return row is null
                ? null
                : Compose(
                    row.SchemeId, row.Description, row.SchemeType, row.MinimumInvestment,
                    row.IncomeInterest, row.GracePeriodMonths, row.InvestmentYears,
                    row.FreeInstallationAmt, row.IsActive, row.CreatedOn, row.UpdatedOn,
                    row.CreatorFirstName, row.CreatorLastName, row.EditorFirstName, row.EditorLastName);
        }

        /// <summary>
        /// The one place a scheme row becomes a scheme DTO, shared by the register and the
        /// single-scheme read so the two cannot drift apart in what they say about the
        /// same row.
        /// </summary>
        private static InvestmentSchemeDTO Compose(
            Guid schemeId,
            string description,
            int schemeType,
            decimal minimumInvestment,
            decimal incomeInterest,
            int gracePeriodMonths,
            int investmentYears,
            decimal freeInstallationAmt,
            bool isActive,
            DateTime? createdOn,
            DateTime? updatedOn,
            string? creatorFirstName,
            string? creatorLastName,
            string? editorFirstName,
            string? editorLastName)
        {
            // A row written before a structure was retired, or by something other than
            // this console, could hold a value the enum no longer names. Falling back to
            // immediate keeps the register readable; the configuration page will refuse to
            // save it again without a valid choice, which is where it should be corrected.
            var type = Enum.IsDefined(typeof(SchemeTypeEnum), schemeType)
                ? (SchemeTypeEnum)schemeType
                : SchemeTypeEnum.ImmediateInstallation;

            return new InvestmentSchemeDTO
            {
                SchemeId = schemeId,
                Description = description,
                SchemeType = schemeType,
                SchemeTypeLabel = type.ToLabel(),
                SchemeTypeTiming = type.ToTiming(),
                MinimumInvestment = minimumInvestment,
                IncomeInterest = incomeInterest,
                GracePeriodMonths = gracePeriodMonths,
                InvestmentYears = investmentYears,
                FreeInstallationAmt = freeInstallationAmt,
                IsActive = isActive,

                // Two states, and they are stated as what the fund does with the scheme
                // rather than as a flag: a withdrawn scheme is not broken, it is simply no
                // longer offered. Amber for the same reason an inactive account is amber —
                // somebody decided this, it is not an alarm.
                Status = isActive ? "On offer" : "Withdrawn",
                StatusTone = isActive ? "success" : "warn",

                CreatedOn = createdOn,
                CreatedByName = DashboardService.ComposeName(creatorFirstName, creatorLastName),
                UpdatedOn = updatedOn,
                UpdatedByName = DashboardService.ComposeName(editorFirstName, editorLastName),
            };
        }
    }
}
