using JianeTech.Api.Authentication;
using JianeTech.Api.Extensions;
using JianeTech.Api.Models.Requests;
using JianeTech.Api.Models.Responses;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JianeTech.Api.Controllers.ApiControllers;

[Route("api/investment-schemes")]
[ApiController]
[Authorize(Roles = ConsoleRoles.Administrator)]
public class InvestmentSchemesApiController : ControllerBase
{
    private readonly IInvestmentSchemeService _investmentSchemeService;
    private readonly ILogger<InvestmentSchemesApiController> _logger;

    public InvestmentSchemesApiController(
        IInvestmentSchemeService investmentSchemeService,
        ILogger<InvestmentSchemesApiController> logger)
    {
        _investmentSchemeService = investmentSchemeService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListInvestmentSchemesRequest request,
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
            request.SchemeType,
            request.MinInvestmentYears,
            request.MaxInvestmentYears,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _investmentSchemeService.GetInvestmentSchemeDTOs(
                request.Page,
                request.PageSize,
                request.Search,
                request.IsActive,
                request.SchemeType,
                request.MinInvestmentYears,
                request.MaxInvestmentYears,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Investment schemes retrieved.",
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
    [HttpGet("{schemeId:guid}")]
    public async Task<IActionResult> Get(Guid schemeId, CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Get);
        var parameters = new { actingUserId = HttpContext.GetActingUserId(), schemeId };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _investmentSchemeService.GetInvestmentSchemeDTOById(
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
                Message = "Investment scheme retrieved.",
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
        [FromBody] SaveInvestmentSchemeRequest request,
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
            request.SchemeType,
            request.MinimumInvestment,
            request.IncomeInterest,
            request.GracePeriodMonths,
            request.InvestmentYears,
            request.FreeInstallationAmt,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var schemeId = await _investmentSchemeService.CreateInvestmentSchemeAsync(
                request.Description,
                request.SchemeType,
                request.MinimumInvestment,
                request.IncomeInterest,
                request.GracePeriodMonths,
                request.InvestmentYears,
                request.FreeInstallationAmt,
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
        catch (InvestmentSchemeValidationException ex)
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
    /// Rewrites a scheme's terms. Capital already placed keeps the figures it was signed
    /// on — <c>dbo.Investments</c> carries them — so this changes what is offered next,
    /// never what somebody already holds.
    /// </summary>
    [HttpPut("{schemeId:guid}")]
    public async Task<IActionResult> Update(
        Guid schemeId,
        [FromBody] SaveInvestmentSchemeRequest request,
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
            request.SchemeType,
            request.MinimumInvestment,
            request.IncomeInterest,
            request.GracePeriodMonths,
            request.InvestmentYears,
            request.FreeInstallationAmt,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _investmentSchemeService.UpdateInvestmentSchemeAsync(
                schemeId,
                request.Description,
                request.SchemeType,
                request.MinimumInvestment,
                request.IncomeInterest,
                request.GracePeriodMonths,
                request.InvestmentYears,
                request.FreeInstallationAmt,
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
        catch (InvestmentSchemeValidationException ex)
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
    /// and every placement made against it stays exactly where it was.
    /// </summary>
    [HttpPut("{schemeId:guid}/active")]
    public async Task<IActionResult> SetActive(
        Guid schemeId,
        [FromBody] SetInvestmentSchemeActiveRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(SetActive);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new { actingUserId, schemeId, request.IsActive };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _investmentSchemeService.SetInvestmentSchemeActiveAsync(
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
        catch (InvestmentSchemeValidationException ex)
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
