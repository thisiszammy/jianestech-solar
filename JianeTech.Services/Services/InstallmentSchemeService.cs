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
    /// The terms a household can be offered: what the installation costs, what is paid up
    /// front, over how many months, and up to what electricity bill it is recommended.
    /// </summary>
    /// <remarks>
    /// Nothing here computes a rate or a schedule. Those are pure functions of the five
    /// stored terms, and the configuration page recomputes them on every keystroke — see
    /// the note on <see cref="InstallmentSchemeDTO"/> for why they are not sent over the
    /// wire as well. The one derived fact this service does enforce is the one that makes a
    /// scheme coherent at all, and it lives in the validator: the payments must at least
    /// cover what is financed.
    /// </remarks>
    public class InstallmentSchemeService : IInstallmentSchemeService
    {
        private const int DefaultPageSize = 20;
        private const int MaxPageSize = 100;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IInstallmentSchemeRepository _schemeRepository;
        private readonly IActivityLogRepository _activityLogRepository;
        private readonly InstallmentSchemeValidator _validator;
        private readonly IClientContextAccessor _clientContextAccessor;
        private readonly IDateTimeService _dateTime;

        public InstallmentSchemeService(
            IUnitOfWork unitOfWork,
            IInstallmentSchemeRepository schemeRepository,
            IActivityLogRepository activityLogRepository,
            InstallmentSchemeValidator validator,
            IClientContextAccessor clientContextAccessor,
            IDateTimeService dateTime)
        {
            _unitOfWork = unitOfWork;
            _schemeRepository = schemeRepository;
            _activityLogRepository = activityLogRepository;
            _validator = validator;
            _clientContextAccessor = clientContextAccessor;
            _dateTime = dateTime;
        }

        public async Task<int> CreateInstallmentSchemeAsync(
            string description,
            decimal principalAmount,
            decimal downPayment,
            decimal periodicPayment,
            int monthPeriods,
            decimal referenceBillAmount,
            Guid? createdBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            // dbo.InstallmentSchemes.SchemeId is an int and is not an identity column — the
            // schema is not this project's to change — so the id is assigned here. Two
            // administrators saving a new scheme in the same instant would read the same
            // maximum; the primary key is the backstop, and the loser gets a 500 rather
            // than a silently duplicated id. With a console this size that trade is the
            // right way round: the alternative is a sequence table this schema does not
            // have, or an identity change it does not own.
            var nextSchemeId = await _schemeRepository.GetInstallmentSchemes()
                .MaxAsync(s => (int?)s.SchemeId, cancellationToken) ?? 0;

            var newScheme = new InstallmentScheme
            {
                SchemeId = nextSchemeId + 1,
                Description = description?.Trim() ?? string.Empty,
                PrincipalAmount = principalAmount,
                DownPayment = downPayment,
                PeriodicPayment = periodicPayment,
                MonthPeriods = monthPeriods,
                ReferenceBillAmount = referenceBillAmount,

                // On offer from the moment it is saved. A scheme nobody can be offered is
                // what the register's off switch is for, not what a new row should be.
                IsActive = true,

                CreatedOn = timestamp,
                CreatedBy = createdBy,
            };

            var createdLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.InstallmentSchemeCreated,
                createdBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.InstallmentSchemeCreated, newScheme.Description),
                ActivityLogReferenceTypeEnum.InstallmentScheme,
                InstallmentScheme.ToAuditReference(newScheme.SchemeId));

            // Validate before the transactional region - an InstallmentSchemeValidationException
            // must never leave an open transaction behind.
            await _validator.ValidateCreateInstallmentScheme(
                newScheme, _schemeRepository.GetInstallmentSchemes(), cancellationToken);

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _schemeRepository.AddInstallmentScheme(newScheme);
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

        public async Task UpdateInstallmentSchemeAsync(
            int schemeId,
            string description,
            decimal principalAmount,
            decimal downPayment,
            decimal periodicPayment,
            int monthPeriods,
            decimal referenceBillAmount,
            Guid? updatedBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var scheme = await _schemeRepository.GetInstallmentSchemes()
                .FirstOrDefaultAsync(s => s.SchemeId == schemeId, cancellationToken);

            // The proposed terms are validated as a detached scheme rather than by
            // assigning them first: a rejected edit must leave the change tracker holding
            // nothing, or the next write in this request would carry it along.
            var proposed = new InstallmentScheme
            {
                SchemeId = schemeId,
                Description = description?.Trim() ?? string.Empty,
                PrincipalAmount = principalAmount,
                DownPayment = downPayment,
                PeriodicPayment = periodicPayment,
                MonthPeriods = monthPeriods,
                ReferenceBillAmount = referenceBillAmount,
            };

            await _validator.ValidateUpdateInstallmentScheme(
                scheme, proposed, _schemeRepository.GetInstallmentSchemes(), cancellationToken);

            scheme.Description = proposed.Description;
            scheme.PrincipalAmount = proposed.PrincipalAmount;
            scheme.DownPayment = proposed.DownPayment;
            scheme.PeriodicPayment = proposed.PeriodicPayment;
            scheme.MonthPeriods = proposed.MonthPeriods;
            scheme.ReferenceBillAmount = proposed.ReferenceBillAmount;
            scheme.UpdatedOn = timestamp;
            scheme.UpdatedBy = updatedBy;

            var updatedLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                ActivityEnum.InstallmentSchemeUpdated,
                updatedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(ActivityEnum.InstallmentSchemeUpdated, scheme.Description),
                ActivityLogReferenceTypeEnum.InstallmentScheme,
                InstallmentScheme.ToAuditReference(scheme.SchemeId));

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _schemeRepository.UpdateInstallmentScheme(scheme);
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

        public async Task SetInstallmentSchemeActiveAsync(
            int schemeId,
            bool isActive,
            Guid? updatedBy,
            CancellationToken cancellationToken)
        {
            var batchId = Guid.NewGuid();
            var executionOrder = 0;
            var timestamp = _dateTime.Now();

            var scheme = await _schemeRepository.GetInstallmentSchemes()
                .FirstOrDefaultAsync(s => s.SchemeId == schemeId, cancellationToken);

            await _validator.ValidateSetInstallmentSchemeActive(
                scheme, isActive, _schemeRepository.GetInstallmentSchemes(), cancellationToken);

            scheme.IsActive = isActive;
            scheme.UpdatedOn = timestamp;
            scheme.UpdatedBy = updatedBy;

            var activity = isActive
                ? ActivityEnum.InstallmentSchemeReactivated
                : ActivityEnum.InstallmentSchemeDeactivated;

            var stateLog = ActivityLog.Create(
                batchId,
                ++executionOrder,
                activity,
                updatedBy,
                timestamp,
                _clientContextAccessor.IpAddress,
                _clientContextAccessor.UserAgent,
                ActivityLog.BuildDetail(activity, scheme.Description),
                ActivityLogReferenceTypeEnum.InstallmentScheme,
                InstallmentScheme.ToAuditReference(scheme.SchemeId));

            try
            {
                await _unitOfWork.BeginTransactionAsync<DatabaseContext>(cancellationToken);

                _schemeRepository.UpdateInstallmentScheme(scheme);
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

        public async Task<QueryableEntityDto<InstallmentSchemeDTO>> GetInstallmentSchemeDTOs(
            int page,
            int pageSize,
            string? search,
            bool? isActive,
            int? minMonthPeriods,
            int? maxMonthPeriods,
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

            var query = _schemeRepository.GetInstallmentSchemes().AsQueryable();

            if (isActive.HasValue)
            {
                query = query.Where(s => s.IsActive == isActive.Value);
            }

            // A band rather than an exact term: the register's filter offers "up to a
            // year", "one to two years" and so on, because nobody looks for a scheme by
            // remembering it was 31 months.
            if (minMonthPeriods.HasValue)
            {
                var min = minMonthPeriods.Value;
                query = query.Where(s => s.MonthPeriods >= min);
            }

            if (maxMonthPeriods.HasValue)
            {
                var max = maxMonthPeriods.Value;
                query = query.Where(s => s.MonthPeriods <= max);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(s => s.Description.Contains(term));
            }

            // Counted off the filtered query before Skip/Take, so the page count the UI
            // derives agrees with the rows it is handed.
            var totalRows = await query.CountAsync(cancellationToken);

            // Newest first, as every register in this console is. SchemeId breaks ties and
            // keeps the order stable across pages for rows that carry no CreatedOn.
            var rows = await query
                .OrderByDescending(s => s.CreatedOn)
                .ThenByDescending(s => s.SchemeId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.SchemeId,
                    s.Description,
                    s.PrincipalAmount,
                    s.DownPayment,
                    s.PeriodicPayment,
                    s.MonthPeriods,
                    s.ReferenceBillAmount,
                    s.IsActive,
                    s.CreatedOn,
                    s.UpdatedOn,
                    CreatorFirstName = s.CreatedByNavigation != null ? s.CreatedByNavigation.FirstName : null,
                    CreatorLastName = s.CreatedByNavigation != null ? s.CreatedByNavigation.LastName : null,
                    EditorFirstName = s.UpdatedByNavigation != null ? s.UpdatedByNavigation.FirstName : null,
                    EditorLastName = s.UpdatedByNavigation != null ? s.UpdatedByNavigation.LastName : null,
                })
                .ToListAsync(cancellationToken);

            // ComposeName and the status ladder are C#, so neither survives translation to
            // SQL - the query above stops at an anonymous type and the display shape is
            // assembled here.
            var items = rows.Select(r => Compose(
                r.SchemeId, r.Description, r.PrincipalAmount, r.DownPayment, r.PeriodicPayment,
                r.MonthPeriods, r.ReferenceBillAmount, r.IsActive, r.CreatedOn, r.UpdatedOn,
                r.CreatorFirstName, r.CreatorLastName, r.EditorFirstName, r.EditorLastName))
                .ToList();

            return new QueryableEntityDto<InstallmentSchemeDTO>
            {
                Items = items,
                TotalRows = totalRows,
            };
        }

        public async Task<InstallmentSchemeDTO?> GetInstallmentSchemeDTOById(
            int schemeId,
            CancellationToken cancellationToken)
        {
            var row = await _schemeRepository.GetInstallmentSchemes()
                .Where(s => s.SchemeId == schemeId)
                .Select(s => new
                {
                    s.SchemeId,
                    s.Description,
                    s.PrincipalAmount,
                    s.DownPayment,
                    s.PeriodicPayment,
                    s.MonthPeriods,
                    s.ReferenceBillAmount,
                    s.IsActive,
                    s.CreatedOn,
                    s.UpdatedOn,
                    CreatorFirstName = s.CreatedByNavigation != null ? s.CreatedByNavigation.FirstName : null,
                    CreatorLastName = s.CreatedByNavigation != null ? s.CreatedByNavigation.LastName : null,
                    EditorFirstName = s.UpdatedByNavigation != null ? s.UpdatedByNavigation.FirstName : null,
                    EditorLastName = s.UpdatedByNavigation != null ? s.UpdatedByNavigation.LastName : null,
                })
                .FirstOrDefaultAsync(cancellationToken);

            return row is null
                ? null
                : Compose(
                    row.SchemeId, row.Description, row.PrincipalAmount, row.DownPayment,
                    row.PeriodicPayment, row.MonthPeriods, row.ReferenceBillAmount, row.IsActive,
                    row.CreatedOn, row.UpdatedOn, row.CreatorFirstName, row.CreatorLastName,
                    row.EditorFirstName, row.EditorLastName);
        }

        /// <summary>
        /// The one place a scheme row becomes a scheme DTO, shared by the register and the
        /// single-scheme read so the two cannot drift apart in what they say about the same
        /// row.
        /// </summary>
        private static InstallmentSchemeDTO Compose(
            int schemeId,
            string description,
            decimal principalAmount,
            decimal downPayment,
            decimal periodicPayment,
            int monthPeriods,
            decimal referenceBillAmount,
            bool isActive,
            DateTime? createdOn,
            DateTime? updatedOn,
            string? creatorFirstName,
            string? creatorLastName,
            string? editorFirstName,
            string? editorLastName)
            => new()
            {
                SchemeId = schemeId,
                Description = description,
                PrincipalAmount = principalAmount,
                DownPayment = downPayment,
                PeriodicPayment = periodicPayment,
                MonthPeriods = monthPeriods,
                ReferenceBillAmount = referenceBillAmount,
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
