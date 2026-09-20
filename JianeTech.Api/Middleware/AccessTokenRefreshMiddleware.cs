using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using JianeTech.Api.Authentication;
using JianeTech.Services.DTOs;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace JianeTech.Api.Middleware;

/// <summary>
/// Keeps a session alive without the client ever knowing. When a request arrives with an
/// expired (or unusable) access-token cookie but a still-valid refresh cookie, this mints
/// a fresh access token, hands it to the JWT bearer handler for this very request, and
/// re-stamps both cookies on the way out. The client makes one request and gets one
/// answer; the rotation is invisible to it.
/// </summary>
public class AccessTokenRefreshMiddleware
{
    private const string AuthenticatePath = "/api/auth/authenticate";
    private const string RefreshPath = "/api/auth/refresh";
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    /// How long a completed refresh stays cached against its refresh-token id. A Blazor
    /// page that fires several API calls at once would otherwise present the same
    /// (now-consumed) grant concurrently and trip replay detection on itself.
    /// </summary>
    private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Set on <see cref="HttpContext.Items"/> after a successful refresh on this request.
    /// The JWT bearer OnChallenge handler reads it to skip its defensive cookie clear:
    /// <c>IResponseCookies.Delete</c> strips matching earlier <c>Append</c>s from the same
    /// response, so clearing there would wipe the cookies just rotated and turn a
    /// recoverable challenge into a permanent sign-out.
    /// </summary>
    public const string RefreshedItemKey = "JianeTech.Api.AccessTokenRefreshed";

    /// <summary>
    /// Carries the freshly minted token to JwtBearer's OnMessageReceived directly. The
    /// request Authorization header is rewritten too, but Items is a flat bag that no
    /// feature wrapper can invalidate mid-pipeline.
    /// </summary>
    public const string RefreshedAccessTokenKey = "JianeTech.Api.RefreshedAccessToken";

    private readonly RequestDelegate _next;
    private readonly ILogger<AccessTokenRefreshMiddleware> _logger;
    private readonly IOptionsMonitor<JwtBearerOptions> _jwtOptionsMonitor;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CancellationToken _appStopping;

    private readonly ConcurrentDictionary<Guid, Lazy<Task<AuthenticationDTO>>> _inFlight = new();

    public AccessTokenRefreshMiddleware(
        RequestDelegate next,
        ILogger<AccessTokenRefreshMiddleware> logger,
        IOptionsMonitor<JwtBearerOptions> jwtOptionsMonitor,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime lifetime)
    {
        _next = next;
        _logger = logger;
        _jwtOptionsMonitor = jwtOptionsMonitor;
        _scopeFactory = scopeFactory;
        _appStopping = lifetime.ApplicationStopping;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Sign-in and the explicit refresh endpoint own their own token handling.
        if (context.Request.Path.StartsWithSegments(AuthenticatePath)
            || context.Request.Path.StartsWithSegments(RefreshPath))
        {
            await _next(context);
            return;
        }

        var refreshCookie = context.Request.Cookies[AuthenticationCookies.RefreshTokenName];
        if (string.IsNullOrEmpty(refreshCookie) || !Guid.TryParse(refreshCookie, out var refreshTokenId))
        {
            await _next(context);
            return;
        }

        if (!ShouldRefresh(ExtractAccessToken(context)))
        {
            await _next(context);
            return;
        }

        AuthenticationDTO? result = null;
        try
        {
            result = await GetOrRefreshAsync(refreshTokenId);
        }
        catch (AuthenticationValidationException ex)
        {
            // The grant is spent, revoked or expired. Drop the cookie so the client stops
            // presenting it and falls through to a clean 401 instead of looping.
            _logger.LogWarning(ex, "Silent refresh failed; clearing the refresh cookie.");
            context.Response.Cookies.Delete(
                AuthenticationCookies.RefreshTokenName, AuthenticationCookies.ClearOptions);
        }
        catch (Exception ex)
        {
            // An infrastructure failure is not the client's fault: continue unauthenticated
            // and let the endpoint's own auth requirements decide the status code.
            _logger.LogError(ex, "Unexpected error during silent access-token refresh; continuing without one.");
        }

        if (result is not null)
        {
            var bearer = BearerPrefix + result.AccessToken;

            context.Request.Headers.Authorization = bearer;

            context.Items[RefreshedAccessTokenKey] = result.AccessToken;
            context.Items[RefreshedItemKey] = true;

            context.Response.Cookies.Append(
                AuthenticationCookies.AccessTokenName,
                result.AccessToken,
                AuthenticationCookies.AccessOptions(result.AccessTokenExpiresOn));

            // Rotate the refresh cookie too, or the browser keeps presenting the consumed
            // one on the next request and trips replay detection.
            context.Response.Cookies.Append(
                AuthenticationCookies.RefreshTokenName,
                result.RefreshTokenId.ToString(),
                AuthenticationCookies.RefreshOptions(result.RefreshTokenExpiresOn));
        }

        await _next(context);
    }

    /// <summary>
    /// Collapses concurrent refreshes of the same grant onto one call. Without this, a
    /// page issuing several API calls at once would rotate the same token in parallel and
    /// the losers would look like replays.
    /// </summary>
    private Task<AuthenticationDTO> GetOrRefreshAsync(Guid refreshTokenId)
    {
        var lazy = _inFlight.GetOrAdd(
            refreshTokenId,
            key => new Lazy<Task<AuthenticationDTO>>(
                () => RunRefreshAsync(key),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private async Task<AuthenticationDTO> RunRefreshAsync(Guid refreshTokenId)
    {
        try
        {
            // A scope of its own: the middleware runs before the request's own scoped
            // services would be appropriate to reuse for a write like this.
            using var scope = _scopeFactory.CreateScope();
            var authenticationService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            return await authenticationService.RefreshTokenAsync(refreshTokenId, _appStopping);
        }
        finally
        {
            // Evict after the grace window rather than immediately, so requests that
            // arrived a moment late still read the cached result instead of replaying.
            _ = Task.Delay(GraceWindow, CancellationToken.None)
                .ContinueWith(
                    previous => _inFlight.TryRemove(refreshTokenId, out _),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
        }
    }

    private static string? ExtractAccessToken(HttpContext context)
    {
        // A programmatic caller may send the token itself.
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header)
            && header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return header[BearerPrefix.Length..].Trim();
        }

        // The browser client has no header to send - its token rides in the HttpOnly
        // cookie. Skipping this read would make every request look tokenless and burn a
        // refresh-grant rotation each time.
        return context.Request.Cookies.TryGetValue(AuthenticationCookies.AccessTokenName, out var cookieToken)
               && !string.IsNullOrEmpty(cookieToken)
            ? cookieToken
            : null;
    }

    private bool ShouldRefresh(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken)) return true;

        var jwtOptions = _jwtOptionsMonitor.Get(JwtBearerDefaults.AuthenticationScheme);
        var validationParameters = jwtOptions.TokenValidationParameters.Clone();

        // Lifetime is checked by hand below: the question here is not "is this token
        // valid" but "has it expired", and a genuinely expired token is the normal case.
        validationParameters.ValidateLifetime = false;

        try
        {
            var handler = new JwtSecurityTokenHandler();
            handler.ValidateToken(accessToken, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
                return true;

            return jwt.ValidTo.ToLocalTime() <= DateTime.Now;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException)
        {
            // Unusable token: malformed, tampered with, or signed for something else. Note
            // that a malformed token throws ArgumentException/FormatException rather than
            // SecurityTokenException, so all three must be caught or a corrupt cookie
            // escapes as a 500.
            //
            // Refresh rather than give up: the refresh grant is the authority, and a
            // successful rotation replaces the bad token outright. Returning false would
            // make one corrupt cookie an unrecoverable sign-out.
            _logger.LogWarning(ex, "Access token unusable; falling back to a refresh.");
            return true;
        }
    }
}
