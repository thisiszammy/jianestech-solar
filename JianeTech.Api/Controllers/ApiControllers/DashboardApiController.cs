using JianeTech.Api.Authentication;
using JianeTech.Api.Extensions;
using JianeTech.Api.Models.Responses;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JianeTech.Api.Controllers.ApiControllers;

[Route("api/dashboard")]
[ApiController]
[Authorize(Roles = ConsoleRoles.Administrator)]
public class DashboardApiController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<DashboardApiController> _logger;

    public DashboardApiController(
        IDashboardService dashboardService,
        ILogger<DashboardApiController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

    [HttpGet("snapshot")]
    public async Task<IActionResult> Snapshot(CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Snapshot);
        var parameters = new { actingUserId = HttpContext.GetActingUserId() };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var snapshot = await _dashboardService.GetDashboardSnapshotDTO(cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Dashboard snapshot retrieved.",
                Data = snapshot,
            });
        }
        catch (Exception ex)
        {
            // DashboardService is read-only and throws no service-scoped exception, so the
            // generic handler is the only catch this endpoint needs.
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
