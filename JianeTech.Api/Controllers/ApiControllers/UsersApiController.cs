using JianeTech.Api.Authentication;
using JianeTech.Api.Extensions;
using JianeTech.Api.Models.Requests;
using JianeTech.Api.Models.Responses;
using JianeTech.Data.Enums;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JianeTech.Api.Controllers.ApiControllers;

[Route("api/users")]
[ApiController]
[Authorize(Roles = ConsoleRoles.Administrator)]
public class UsersApiController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IAccountActivationService _accountActivationService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly ILogger<UsersApiController> _logger;

    public UsersApiController(
        IUserService userService,
        IAccountActivationService accountActivationService,
        IPasswordResetService passwordResetService,
        ILogger<UsersApiController> logger)
    {
        _userService = userService;
        _accountActivationService = accountActivationService;
        _passwordResetService = passwordResetService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] ListUsersRequest request,
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
            request.UserType,
            request.IsActive,
            request.IsLocked,
            request.IsPending,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _userService.GetUserDTOs(
                request.Page,
                request.PageSize,
                request.Search,
                request.UserType,
                request.IsActive,
                request.IsLocked,
                request.IsPending,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Users retrieved.",
                Data = result,
            });
        }
        catch (Exception ex)
        {
            // GetUserDTOs is a read and throws no service-scoped exception, so the generic
            // handler is the only catch this endpoint needs.
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
    /// The role picker's option list. Served rather than restated in the client so the
    /// wording stays owned by <c>UserTypeEnumExtensions</c>.
    /// </summary>
    [HttpGet("roles")]
    public IActionResult Roles()
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Roles);
        var parameters = new { actingUserId = HttpContext.GetActingUserId() };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = _userService.GetUserTypeOptionDTOs();

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Roles retrieved.",
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

    /// <summary>
    /// Creates an account and mails its holder a one-time link to choose a password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rate-limited on the general policy rather than <c>auth-strict</c>. No credential is
    /// submitted or checked here — the invitee chooses their own password later, through
    /// <c>AccountApiController</c>, which is on the strict limiter because that is where
    /// the anonymous guessing surface actually is. At five a minute an administrator
    /// onboarding a team would be told "too many attempts" halfway through doing exactly
    /// what the page is for.
    /// </para>
    /// <para>
    /// Answers 200 even when the mail failed, and says so in the message. The account is
    /// real either way, with a live invitation against it — see
    /// <c>InvitationResultDTO</c> for why the two outcomes are reported separately. A 400
    /// here would mean "nothing happened", and something did.
    /// </para>
    /// </remarks>
    [EnableRateLimiting("auth-general")]
    [HttpPost]
    public async Task<IActionResult> Invite(
        [FromBody] InviteUserRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Invite);

        // Resolved from the token, never from the body, and left null when the actor is
        // unknown so the audit column persists as SQL NULL.
        var actingUserId = HttpContext.GetActingUserId();

        var parameters = new
        {
            actingUserId,
            request.FirstName,
            request.LastName,
            request.Username,
            request.Email,
            request.PhoneNumber,
            request.UserType,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _userService.InviteUserAsync(
                request.FirstName,
                request.LastName,
                request.Username,
                request.Email,
                request.PhoneNumber ?? string.Empty,
                (UserTypeEnum)request.UserType,
                actingUserId,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            var name = $"{request.FirstName} {request.LastName}".Trim() is { Length: > 0 } fullName
                ? fullName
                : request.Username;

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = result.EmailSent
                    ? $"{name} has been invited. They can sign in once they set a password."
                    : $"{name}'s account was created, but the invitation could not be sent. "
                      + $"{result.EmailError} Use Resend invitation on their row to try again.",
                Data = result,
            });
        }
        catch (UserValidationException ex)
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

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> Update(
        Guid userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Update);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new
        {
            actingUserId,
            userId,
            request.FirstName,
            request.LastName,
            request.Username,
            request.Email,
            request.PhoneNumber,
            request.UserType,
        };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _userService.UpdateUserAsync(
                userId,
                request.FirstName,
                request.LastName,
                request.Username,
                request.Email,
                request.PhoneNumber ?? string.Empty,
                (UserTypeEnum)request.UserType,
                actingUserId,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = $"{request.FirstName} {request.LastName}".Trim() is { Length: > 0 } name
                    ? $"{name}'s details were updated."
                    : "The account was updated.",
                Data = new { UserId = userId },
            });
        }
        catch (UserValidationException ex)
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
    /// What the register calls delete. Nothing is removed: the account is switched off, so
    /// it can no longer sign in or refresh a session, and every row it owns — including its
    /// own audit trail — stays exactly where it was.
    /// </summary>
    [HttpPut("{userId:guid}/active")]
    public async Task<IActionResult> SetActive(
        Guid userId,
        [FromBody] SetUserActiveRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(SetActive);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new { actingUserId, userId, request.IsActive };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _userService.SetUserActiveAsync(
                userId, request.IsActive, actingUserId, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = request.IsActive
                    ? "The account was reactivated and can sign in again."
                    : "The account was deactivated and can no longer sign in.",
                Data = new { UserId = userId, request.IsActive },
            });
        }
        catch (UserValidationException ex)
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
    /// Issues a fresh invitation for someone who has not set a password yet, voids any
    /// still outstanding, and mails the new one.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="Invite"/>, a delivery failure here is a 400: sending the mail is
    /// the entire point of the call, and the administrator who pressed the button is
    /// watching. The new link is committed before the send is attempted, so pressing it
    /// again after a failure is safe and is the right next move.
    /// </remarks>
    [EnableRateLimiting("auth-general")]
    [HttpPost("{userId:guid}/invitation")]
    public async Task<IActionResult> ResendInvitation(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(ResendInvitation);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new { actingUserId, userId };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _accountActivationService.ResendInvitationAsync(userId, actingUserId, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "The invitation was sent again. Any earlier link for this account no longer works.",
                Data = new { UserId = userId },
            });
        }
        catch (AccountActivationException ex)
        {
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (EmailDeliveryException ex)
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
    /// Mails someone a link to set a new password, on an administrator's behalf.
    /// </summary>
    /// <remarks>
    /// The administrator never learns the password: this issues the same one-time link the
    /// forgot-password form does, and the account holder chooses the value. The existing
    /// password keeps working until that link is spent, so pressing this does not lock
    /// anyone out of anything.
    /// </remarks>
    [EnableRateLimiting("auth-general")]
    [HttpPost("{userId:guid}/password-reset")]
    public async Task<IActionResult> SendPasswordReset(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(SendPasswordReset);
        var actingUserId = HttpContext.GetActingUserId();
        var parameters = new { actingUserId, userId };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _passwordResetService.SendPasswordResetAsync(userId, actingUserId, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "A link to set a new password is on its way to the address on file.",
                Data = new { UserId = userId },
            });
        }
        catch (PasswordResetException ex)
        {
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return BadRequest(new ApiResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Message = ex.Message,
                Data = null,
            });
        }
        catch (EmailDeliveryException ex)
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
