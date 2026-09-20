using JianeTech.Api.Authentication;
using JianeTech.Api.Extensions;
using JianeTech.Api.Models.Requests;
using JianeTech.Api.Models.Responses;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JianeTech.Api.Controllers.ApiControllers;

[Route("api/auth")]
[ApiController]
public class AuthenticationApiController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;
    private readonly ILogger<AuthenticationApiController> _logger;

    public AuthenticationApiController(
        IAuthenticationService authenticationService,
        ILogger<AuthenticationApiController> logger)
    {
        _authenticationService = authenticationService;
        _logger = logger;
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth-strict")]
    [HttpPost("authenticate")]
    public async Task<IActionResult> Authenticate(
        [FromBody] AuthenticateUserRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Authenticate);

        // Built explicitly so the password cannot ride along into the log.
        var parameters = new
        {
            request.Username,
            request.RememberMe,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _authenticationService.AuthenticateUserAsync(
                request.Username,
                request.Password,
                request.RememberMe,
                cancellationToken);

            SetAccessTokenCookie(result.AccessToken, result.AccessTokenExpiresOn);
            SetRefreshTokenCookie(result.RefreshTokenId, result.RefreshTokenExpiresOn);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Signed in successfully.",
                Data = new AuthenticationResponse
                {
                    AccessTokenExpiresOn = result.AccessTokenExpiresOn,
                    User = result.User,
                },
            });
        }
        catch (AccountLockedException ex)
        {
            // Caught ahead of the base type so the client can render the lock-out state
            // rather than a generic credential error.
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (AuthenticationValidationException ex)
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

    [AllowAnonymous]
    [EnableRateLimiting("auth-general")]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Refresh);
        var refreshCookie = Request.Cookies[AuthenticationCookies.RefreshTokenName];

        // The token id is a bearer secret in its own right - log only whether one arrived.
        var parameters = new { HasRefreshCookie = !string.IsNullOrEmpty(refreshCookie) };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        if (!Guid.TryParse(refreshCookie, out var refreshTokenId))
        {
            var missing = new AuthenticationValidationException("No active session. Please sign in.");
            _logger.LogWarning(missing, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

            ClearAuthenticationCookies();

            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = missing.Message,
                Data = null,
            });
        }

        try
        {
            var result = await _authenticationService.RefreshTokenAsync(refreshTokenId, cancellationToken);

            SetAccessTokenCookie(result.AccessToken, result.AccessTokenExpiresOn);
            SetRefreshTokenCookie(result.RefreshTokenId, result.RefreshTokenExpiresOn);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Session refreshed.",
                Data = new AuthenticationResponse
                {
                    AccessTokenExpiresOn = result.AccessTokenExpiresOn,
                    User = result.User,
                },
            });
        }
        catch (RefreshTokenReplayException ex)
        {
            // Every live grant for this account was already revoked by the service. Clear
            // the cookies so the browser stops presenting a grant that can never work.
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            ClearAuthenticationCookies();
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (AuthenticationValidationException ex)
        {
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            ClearAuthenticationCookies();
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

    [Authorize]
    [EnableRateLimiting("auth-general")]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Me);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new { actingUserId };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            if (actingUserId is null)
            {
                return Unauthorized(new ApiResponse
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Message = "No active session.",
                    Data = null,
                });
            }

            var user = await _authenticationService.GetAuthenticatedUserDTOById(
                actingUserId.Value, cancellationToken);

            if (user is null)
            {
                // The token validated but its subject is gone (retired between issuance and
                // now). Treat it as no session rather than a server fault.
                return Unauthorized(new ApiResponse
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Message = "No active session.",
                    Data = null,
                });
            }

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Session retrieved.",
                Data = user,
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

    [Authorize]
    [EnableRateLimiting("auth-general")]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Logout);
        var actingUserId = HttpContext.GetActingUserId();
        var refreshCookie = Request.Cookies[AuthenticationCookies.RefreshTokenName];
        var parameters = new
        {
            actingUserId,
            HasRefreshCookie = !string.IsNullOrEmpty(refreshCookie),
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        // Cleared before the service call: whatever happens server-side, this browser is
        // signed out when the response lands.
        ClearAuthenticationCookies();

        if (!Guid.TryParse(refreshCookie, out var refreshTokenId))
        {
            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);
            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Already signed out.",
                Data = null,
            });
        }

        try
        {
            await _authenticationService.LogoutAsync(refreshTokenId, actingUserId, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Signed out.",
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

    private void SetAccessTokenCookie(string token, DateTime expiresOn)
        => Response.Cookies.Append(
            AuthenticationCookies.AccessTokenName,
            token,
            AuthenticationCookies.AccessOptions(expiresOn));

    private void SetRefreshTokenCookie(Guid refreshTokenId, DateTime expiresOn)
        => Response.Cookies.Append(
            AuthenticationCookies.RefreshTokenName,
            refreshTokenId.ToString(),
            AuthenticationCookies.RefreshOptions(expiresOn));

    private void ClearAuthenticationCookies()
    {
        Response.Cookies.Delete(AuthenticationCookies.AccessTokenName, AuthenticationCookies.ClearOptions);
        Response.Cookies.Delete(AuthenticationCookies.RefreshTokenName, AuthenticationCookies.ClearOptions);
    }
}
