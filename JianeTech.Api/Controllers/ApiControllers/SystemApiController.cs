using JianeTech.Api.Models.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JianeTech.Api.Controllers.ApiControllers;

/// <summary>
/// What this host says about itself. One fact so far: the version it is running.
/// </summary>
/// <remarks>
/// <para>
/// Anonymous, because the two pages that print the version — the landing page and the
/// sign-in screen — are both read before anybody has a session.
/// </para>
/// <para>
/// There is no service behind this controller, and that is not an omission. The version
/// is a constant on the host's own <see cref="Program"/>; it is metadata about the
/// process, not business logic, and <c>JianeTech.Services</c> has nothing to add to it.
/// </para>
/// </remarks>
[Route("api/system")]
[ApiController]
[AllowAnonymous]
public class SystemApiController : ControllerBase
{
    private readonly ILogger<SystemApiController> _logger;

    public SystemApiController(ILogger<SystemApiController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The display version of this API — <see cref="Program.Version"/>, over the wire.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The console cannot know this any other way. A Debug build serves the console from
    /// this host, so the two halves are always the same build; a Release build ships them
    /// separately, and that is exactly when somebody needs to know which API is answering.
    /// </para>
    /// <para>
    /// On <c>auth-general</c>, like the other cheap anonymous reads: nobody needs this
    /// sixty times a minute, and every call writes log lines. <b>Never cached</b> — the
    /// one job of this answer is to say what is running <em>now</em>, and an anonymous,
    /// cookie-less GET is precisely what a proxy in front of a Release deployment would
    /// otherwise feel free to keep.
    /// </para>
    /// </remarks>
    [EnableRateLimiting("auth-general")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [HttpGet("version")]
    public IActionResult Version()
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Version);

        // The endpoint takes no input, so there is nothing to record beyond the call.
        var parameters = new { };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var version = new VersionResponse { Version = Program.Version };

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Version retrieved.",
                Data = version,
            });
        }
        catch (Exception ex)
        {
            // No service is called, so there is no service-scoped exception to catch.
            // Reading a constant cannot throw either, but no exception may escape an
            // endpoint, and this one does not get to be the exception to that.
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
