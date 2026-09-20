using JianeTech.Api.Models.Requests;
using JianeTech.Api.Models.Responses;
using JianeTech.Data.Enums;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace JianeTech.Api.Controllers.ApiControllers;

/// <summary>
/// The signed-out half of account management: opening an invitation, choosing a first
/// password, asking for a recovery link and spending one.
/// </summary>
/// <remarks>
/// <para>
/// Every endpoint here is anonymous by necessity — the whole point is that the caller
/// cannot sign in yet. What stands in for a session is the token in the route, which is
/// single-use, short-lived and checked against a stored hash rather than a stored copy.
/// </para>
/// <para>
/// <b>The token is never logged.</b> It is the credential for these calls in exactly the
/// way a password is for sign-in, so the parameter objects below record only whether one
/// was supplied — the same discipline <c>AuthenticationApiController.Refresh</c> applies
/// to the refresh cookie.
/// </para>
/// </remarks>
[Route("api/account")]
[ApiController]
[AllowAnonymous]
public class AccountApiController : ControllerBase
{
    private readonly IAccountActivationService _accountActivationService;
    private readonly IPasswordResetService _passwordResetService;
    private readonly IUserService _userService;
    private readonly ILogger<AccountApiController> _logger;

    public AccountApiController(
        IAccountActivationService accountActivationService,
        IPasswordResetService passwordResetService,
        IUserService userService,
        ILogger<AccountApiController> logger)
    {
        _accountActivationService = accountActivationService;
        _passwordResetService = passwordResetService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Opens an account for a visitor from the public site and mails them a one-time
    /// link to choose a password.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one endpoint here that runs before any token exists, which is why it sits
    /// first. On <c>auth-strict</c> rather than <c>auth-general</c>: this is an anonymous
    /// write that also sends mail, so it is the most abusable surface the app has, and
    /// nobody legitimately opens six accounts a minute. <c>UsersApiController.Invite</c>
    /// is on the looser policy because an administrator onboarding a team genuinely does.
    /// </para>
    /// <para>
    /// <b>Success and "that address already has an account" answer identically.</b> Any
    /// difference between them turns this into a way of testing which addresses belong to
    /// the fund's investors and customers — the same reason
    /// <see cref="RequestPasswordReset"/> is uninformative, and the reason
    /// <c>DuplicateAccountException</c> exists as a type of its own. The real outcome goes
    /// to the log.
    /// </para>
    /// <para>
    /// The one answer that does differ is an account created whose mail did not leave.
    /// That is reported plainly, because the alternative is telling somebody to check an
    /// inbox nothing was sent to. It discloses a new address only while the mail provider
    /// is down, which is not a condition a prober can bring about. Response timing is
    /// likewise not levelled — a duplicate returns before the call to Brevo — and the
    /// same is already true of the password-reset endpoint.
    /// </para>
    /// </remarks>
    [EnableRateLimiting("auth-strict")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Register);

        // No credential is submitted here, so every field on the form is loggable. Note
        // what is not read: the body carries no role and no acting user id.
        var parameters = new
        {
            request.FirstName,
            request.LastName,
            request.Username,
            request.Email,
            request.PhoneNumber,
            request.Interest,
        };

        // The one sentence this endpoint gives a stranger, whether an account was just
        // opened or the address already had one. Declared once so the two paths cannot
        // drift apart into an oracle.
        const string neutralAnswer =
            "Thanks. If that email address is new to the fund, a link to set your password "
            + "is on its way to it. If you already have an account, sign in instead.";

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _userService.SelfRegisterUserAsync(
                request.FirstName,
                request.LastName,
                request.Username,
                request.Email,
                request.PhoneNumber ?? string.Empty,
                Enum.IsDefined((RegistrationInterestEnum)request.Interest)
                    ? (RegistrationInterestEnum)request.Interest
                    : RegistrationInterestEnum.Unspecified,
                cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            if (!result.EmailSent)
            {
                return Ok(new ApiResponse
                {
                    Code = StatusCodes.Status200OK,
                    Message = $"Your account was created, but the email could not be sent. "
                              + $"{result.EmailError} Ask the fund to send your link again.",
                    Data = result,
                });
            }

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = neutralAnswer,
                Data = null,
            });
        }
        catch (DuplicateAccountException ex)
        {
            // Caught before the base type, and answered 200 rather than 400. Nothing was
            // created and the exception says which address; neither fact reaches the
            // caller. See the remarks above.
            _logger.LogWarning(ex, "{LogId} : {Process} -> {@Parameters}", logId, process, parameters);
            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = neutralAnswer,
                Data = null,
            });
        }
        catch (UserValidationException ex)
        {
            // A field that is missing, too long, malformed, or a username already taken.
            // All of these are about what was typed, so all of them are reported.
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
    /// Who an invitation link belongs to. The set-a-password page calls this first so it
    /// can greet the reader by name and, more usefully, refuse early with the real reason
    /// rather than rendering a form whose submit is guaranteed to fail.
    /// </summary>
    [EnableRateLimiting("auth-general")]
    [HttpGet("activation/{token:guid}")]
    public async Task<IActionResult> Invitation(Guid token, CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Invitation);
        var parameters = new { tokenSupplied = token != Guid.Empty };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _accountActivationService.GetInvitationDTOByToken(token, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Invitation retrieved.",
                Data = result,
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
    /// Spends an invitation: sets the password the invitee chose. On the strict limiter,
    /// because the route parameter is a secret and this is where guesses would be aimed.
    /// </summary>
    [EnableRateLimiting("auth-strict")]
    [HttpPost("activation/{token:guid}")]
    public async Task<IActionResult> Activate(
        Guid token,
        [FromBody] SetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(Activate);

        // Built explicitly so neither the token nor the password can ride along.
        var parameters = new { tokenSupplied = token != Guid.Empty };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _accountActivationService.ActivateAccountAsync(token, request.Password, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Your password is set. You can sign in now.",
                Data = null,
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
    /// Asks for a recovery link.
    /// </summary>
    /// <remarks>
    /// Answers 200 with the same sentence whatever happened — unknown identifier, inactive
    /// account, invitation still outstanding, mail that bounced. That is not vagueness for
    /// its own sake: any answer that varied with the account's existence would turn this
    /// endpoint into a way of testing which usernames and addresses are real, which is
    /// also why the sign-in form refuses a wrong password and an unknown user identically.
    /// The service records the real outcome in the log.
    /// </remarks>
    [EnableRateLimiting("auth-strict")]
    [HttpPost("password-reset")]
    public async Task<IActionResult> RequestPasswordReset(
        [FromBody] RequestPasswordResetRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(RequestPasswordReset);
        var parameters = new { request.Identifier };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _passwordResetService.RequestPasswordResetAsync(request.Identifier, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "If that account exists, a link to set a new password is on its way to the address on file.",
                Data = null,
            });
        }
        catch (Exception ex)
        {
            // RequestPasswordResetAsync throws no service-scoped exception: every refusal
            // it makes is a silent one, for the reason above.
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
    /// Who a recovery link belongs to — the same early check the invitation page makes.
    /// </summary>
    [EnableRateLimiting("auth-general")]
    [HttpGet("password-reset/{token:guid}")]
    public async Task<IActionResult> PasswordReset(Guid token, CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(PasswordReset);
        var parameters = new { tokenSupplied = token != Guid.Empty };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            var result = await _passwordResetService.GetPasswordResetTokenDTOByToken(token, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Reset link retrieved.",
                Data = result,
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
    /// Spends a recovery link. Every session opened with the old password ends with it —
    /// see <c>PasswordResetService.ResetPasswordAsync</c>.
    /// </summary>
    [EnableRateLimiting("auth-strict")]
    [HttpPost("password-reset/{token:guid}")]
    public async Task<IActionResult> ResetPassword(
        Guid token,
        [FromBody] SetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var logId = Guid.NewGuid();
        const string process = nameof(ResetPassword);
        var parameters = new { tokenSupplied = token != Guid.Empty };

        _logger.LogInformation("{LogId} : {Process} -> {@Parameters}", logId, process, parameters);

        try
        {
            await _passwordResetService.ResetPasswordAsync(token, request.Password, cancellationToken);

            _logger.LogInformation("{LogId} : {Process} succeeded -> {@Parameters}", logId, process, parameters);

            return Ok(new ApiResponse
            {
                Code = StatusCodes.Status200OK,
                Message = "Your password has been changed. You can sign in now.",
                Data = null,
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
