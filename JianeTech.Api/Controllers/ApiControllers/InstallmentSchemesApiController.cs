using JianeTech.Api.Authentication;
using JianeTech.Api.Extensions;
using JianeTech.Api.Models.Requests;
using JianeTech.Api.Models.Responses;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JianeTech.Api.Controllers.ApiControllers;

[Route("api/installment-schemes")]
[ApiController]
[Authorize(Roles = ConsoleRoles.Administrator)]
public class InstallmentSchemesApiController : ControllerBase
{
    private readonly IInstallmentSchemeService _installmentSchemeService;
    private readonly ILogger<InstallmentSchemesApiController> _logger;

    public InstallmentSchemesApiController(
        IInstallmentSchemeService installmentSchemeService,
        ILogger<InstallmentSchemesApiController> logger)
    {
        _installmentSchemeService = installmentSchemeService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListInstallmentSchemesRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(List);
        var parameters = new
        {
            actingUserId = HttpContext.GetActingUserId(),
            request.Page,
            request.PageSize,
            request.Search,
            request.IsActive,
            request.MinMonthPeriods,
            request.MaxMonthPeriods,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _installmentSchemeService.GetInstallmentSchemeDTOs(
                request.Page,
                request.PageSize,
                request.Search,
                request.IsActive,
                request.MinMonthPeriods,
                request.MaxMonthPeriods,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Installment schemes retrieved.",
                Data = result,
            });
        }
        catch (Exception ex)
        {
            // A read, and it throws no service-scoped exception, so the generic handler is
            // the only catch this endpoint needs.
            _logger.LogError(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Message = "Unknown server error occurred",
                Data = null,
            });
        }
    }

    /// <summary>
    /// One scheme, for the configuration page to open. A 404 rather than an empty form,
    /// so a stale link says what happened instead of offering to create something.
    /// </summary>
    [HttpGet("{schemeId:int}")]
    public async Task<IActionResult> Get(int schemeId, CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Get);
        var parameters = new { actingUserId = HttpContext.GetActingUserId(), schemeId };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _installmentSchemeService.GetInstallmentSchemeDTOById(
                schemeId, cancellationToken);

            if (result is null)
            {
                _logger.LogInformation(
                    "{LogId} : {Process} found nothing -> {@Parameters}", logId, process, parameters);

                return NotFound(new ApiResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Message = "That scheme no longer exists.",
                    Data = null,
                });
            }

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Installment scheme retrieved.",
                Data = result,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Message = "Unknown server error occurred",
                Data = null,
            });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] SaveInstallmentSchemeRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Create);

        // Resolved from the token, never from the body, and left null when the actor is
        // unknown so the audit column persists as SQL NULL.
        var actingUserId = HttpContext.GetActingUserId();

        var parameters = new
        {
            actingUserId,
            request.Description,
            request.PrincipalAmount,
            request.DownPayment,
            request.PeriodicPayment,
            request.MonthPeriods,
            request.ReferenceBillAmount,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var schemeId = await _installmentSchemeService.CreateInstallmentSchemeAsync(
                request.Description,
                request.PrincipalAmount,
                request.DownPayment,
                request.PeriodicPayment,
                request.MonthPeriods,
                request.ReferenceBillAmount,
                actingUserId,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = $"'{request.Description.Trim()}' is now on offer.",
                Data = new { SchemeId = schemeId },
            });
        }
        catch (InstallmentSchemeValidationException ex)
        {
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Message = "Unknown server error occurred",
                Data = null,
            });
        }
    }

    /// <summary>
    /// Rewrites a scheme's terms. Agreements already written against it keep the figures
    /// they were sold on — <c>dbo.Installments</c> copies them — so this changes what is
    /// offered next, never what somebody already signed.
    /// </summary>
    [HttpPut("{schemeId:int}")]
    public async Task<IActionResult> Update(
        int schemeId,
        [FromBody] SaveInstallmentSchemeRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Update);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new
        {
            actingUserId,
            schemeId,
            request.Description,
            request.PrincipalAmount,
            request.DownPayment,
            request.PeriodicPayment,
            request.MonthPeriods,
            request.ReferenceBillAmount,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _installmentSchemeService.UpdateInstallmentSchemeAsync(
                schemeId,
                request.Description,
                request.PrincipalAmount,
                request.DownPayment,
                request.PeriodicPayment,
                request.MonthPeriods,
                request.ReferenceBillAmount,
                actingUserId,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = $"The terms of '{request.Description.Trim()}' were saved.",
                Data = new { SchemeId = schemeId },
            });
        }
        catch (InstallmentSchemeValidationException ex)
        {
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Message = "Unknown server error occurred",
                Data = null,
            });
        }
    }

    /// <summary>
    /// What the register calls delete. Nothing is removed: the scheme stops being offered,
    /// and every agreement written against it stays exactly where it was.
    /// </summary>
    [HttpPut("{schemeId:int}/active")]
    public async Task<IActionResult> SetActive(
        int schemeId,
        [FromBody] SetInstallmentSchemeActiveRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(SetActive);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new { actingUserId, schemeId, request.IsActive };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _installmentSchemeService.SetInstallmentSchemeActiveAsync(
                schemeId, request.IsActive, actingUserId, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = request.IsActive
                    ? "The scheme is on offer again."
                    : "The scheme was withdrawn and will not be offered to anyone new.",
                Data = new { SchemeId = schemeId, request.IsActive },
            });
        }
        catch (InstallmentSchemeValidationException ex)
        {
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return StatusCode(StatusCodes.Status500InternalServerError, new ApiResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Message = "Unknown server error occurred",
                Data = null,
            });
        }
    }
}
