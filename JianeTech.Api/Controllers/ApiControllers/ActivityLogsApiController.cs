using JianeTech.Api.Authentication;
using JianeTech.Api.Extensions;
using JianeTech.Api.Models.Requests;
using JianeTech.Api.Models.Responses;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JianeTech.Api.Controllers.ApiControllers;

[Route("api/activity-logs")]
[ApiController]
[Authorize(Roles = ConsoleRoles.Administrator)]
public class ActivityLogsApiController : ControllerBase
{
    private readonly IActivityLogService _activityLogService;
    private readonly ILogger<ActivityLogsApiController> _logger;

    public ActivityLogsApiController(
        IActivityLogService activityLogService,
        ILogger<ActivityLogsApiController> logger)
    {
        _activityLogService = activityLogService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListActivityLogsRequest request,
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
            request.Activity,
            request.From,
            request.To,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _activityLogService.GetActivityLogBatchDTOs(
                request.Page,
                request.PageSize,
                request.Search,
                request.Activity,
                request.From,
                request.To,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Activity logs retrieved.",
                Data = result,
            });
        }
        catch (Exception ex)
        {
            // ActivityLogService is read-only and throws no service-scoped exception, so
            // the generic handler is the only catch this endpoint needs.
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
    /// The event filter's option list. Served rather than restated in the client so the
    /// wording stays owned by <c>ActivityEnumExtensions</c> and cannot drift from the rows.
    /// </summary>
    [HttpGet("activities")]
    public IActionResult Activities()
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Activities);
        var parameters = new { actingUserId = HttpContext.GetActingUserId() };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = _activityLogService.GetActivityOptionDTOs();

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Activities retrieved.",
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
}
