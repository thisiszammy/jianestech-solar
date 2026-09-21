using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using JianeTech.Client.Auth.Models;

namespace JianeTech.Client.Auth;

/// <summary>
/// Every call the console makes. Each returns the API's own <see cref="ApiResponse{T}"/>
/// envelope rather than throwing, so a page can render the server's message verbatim —
/// "Invalid username or password." reaches the form exactly as the API worded it.
/// </summary>
public class FundApiClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public FundApiClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResponse<AuthenticationResponse>> AuthenticateAsync(
        string username, string password, bool rememberMe, CancellationToken cancellationToken = default)
        => PostAsync<AuthenticationResponse>(
            "api/auth/authenticate",
            new AuthenticateRequest { Username = username, Password = password, RememberMe = rememberMe },
            cancellationToken);

    public Task<ApiResponse<AuthenticatedUser>> MeAsync(CancellationToken cancellationToken = default)
        => GetAsync<AuthenticatedUser>("api/auth/me", cancellationToken);

    public Task<ApiResponse<AuthenticationResponse>> RefreshAsync(CancellationToken cancellationToken = default)
        => PostAsync<AuthenticationResponse>("api/auth/refresh", null, cancellationToken);

    public Task<ApiResponse<object>> LogoutAsync(CancellationToken cancellationToken = default)
        => PostAsync<object>("api/auth/logout", null, cancellationToken);

    public Task<ApiResponse<DashboardSnapshot>> GetDashboardSnapshotAsync(
        CancellationToken cancellationToken = default)
        => GetAsync<DashboardSnapshot>("api/dashboard/snapshot", cancellationToken);

    public Task<ApiResponse<QueryableEntity<ActivityLogBatch>>> GetActivityLogsAsync(
        int page,
        int pageSize,
        string? search,
        int? activity,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        // Only the filters that are actually set are sent. An absent parameter and one
        // present-but-empty mean the same thing to the API, but the shorter URL is the
        // one that shows up legibly in a network log.
        var url = $"api/activity-logs?page={page}&pageSize={pageSize}"
            + Param("search", search)
            + Param("activity", activity)
            + Param("from", from)
            + Param("to", to);

        return GetAsync<QueryableEntity<ActivityLogBatch>>(url, cancellationToken);
    }

    public Task<ApiResponse<List<ActivityOption>>> GetActivityOptionsAsync(
        CancellationToken cancellationToken = default)
        => GetAsync<List<ActivityOption>>("api/activity-logs/activities", cancellationToken);

    public Task<ApiResponse<QueryableEntity<UserRow>>> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        int? userType,
        bool? isActive,
        bool? isLocked,
        bool? isPending,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/users?page={page}&pageSize={pageSize}"
            + Param("search", search)
            + Param("userType", userType)
            + Param("isActive", isActive)
            + Param("isLocked", isLocked)
            + Param("isPending", isPending);

        return GetAsync<QueryableEntity<UserRow>>(url, cancellationToken);
    }

    public Task<ApiResponse<List<RoleOption>>> GetRolesAsync(
        CancellationToken cancellationToken = default)
        => GetAsync<List<RoleOption>>("api/users/roles", cancellationToken);

    /// <summary>
    /// Creates the account and mails its holder a link to choose a password. Answers 200
    /// even when the mail failed - the account is real either way - so the caller reads
    /// <c>InvitationResult.EmailSent</c> rather than the status code to know.
    /// </summary>
    public Task<ApiResponse<InvitationResult>> InviteUserAsync(
        InviteUserRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InvitationResult>("api/users", request, cancellationToken);

    /// <summary>Issues a fresh invitation and voids any still outstanding.</summary>
    public Task<ApiResponse<CreatedUser>> ResendInvitationAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => PostAsync<CreatedUser>($"api/users/{userId}/invitation", null, cancellationToken);

    /// <summary>
    /// Mails someone a link to set a new password. The administrator never learns the
    /// value, and the existing password keeps working until the link is spent.
    /// </summary>
    public Task<ApiResponse<CreatedUser>> SendPasswordResetAsync(
        Guid userId, CancellationToken cancellationToken = default)
        => PostAsync<CreatedUser>($"api/users/{userId}/password-reset", null, cancellationToken);

    // --- Signed-out flows, reached from a link in an email -------------------------
    // Each takes the raw token from the address bar. It is the credential for these
    // calls, so it goes in the route and is never put anywhere it would be logged.

    public Task<ApiResponse<AccountLink>> GetInvitationAsync(
        Guid token, CancellationToken cancellationToken = default)
        => GetAsync<AccountLink>($"api/account/activation/{token}", cancellationToken);

    public Task<ApiResponse<object>> ActivateAccountAsync(
        Guid token, string password, CancellationToken cancellationToken = default)
        => PostAsync<object>(
            $"api/account/activation/{token}",
            new SetPasswordRequest { Password = password },
            cancellationToken);

    /// <summary>
    /// Opens an account from the public site.
    /// </summary>
    /// <remarks>
    /// Answers 200 with one sentence whether the account was just created or the address
    /// already had one — the endpoint will not confirm which, and neither can this. The
    /// only 200 that says anything more is an account whose mail failed, and that one
    /// arrives with <c>Data.EmailSent</c> false. A 400 is always about what was typed.
    /// </remarks>
    public Task<ApiResponse<InvitationResult>> RegisterAccountAsync(
        RegisterAccountRequest request, CancellationToken cancellationToken = default)
        => PostAsync<InvitationResult>("api/account/register", request, cancellationToken);

    public Task<ApiResponse<object>> RequestPasswordResetAsync(
        string identifier, CancellationToken cancellationToken = default)
        => PostAsync<object>(
            "api/account/password-reset",
            new RequestPasswordResetRequest { Identifier = identifier },
            cancellationToken);

    public Task<ApiResponse<AccountLink>> GetPasswordResetAsync(
        Guid token, CancellationToken cancellationToken = default)
        => GetAsync<AccountLink>($"api/account/password-reset/{token}", cancellationToken);

    public Task<ApiResponse<object>> ResetPasswordAsync(
        Guid token, string password, CancellationToken cancellationToken = default)
        => PostAsync<object>(
            $"api/account/password-reset/{token}",
            new SetPasswordRequest { Password = password },
            cancellationToken);

    public Task<ApiResponse<CreatedUser>> UpdateUserAsync(
        Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
        => PutAsync<CreatedUser>($"api/users/{userId}", request, cancellationToken);

    /// <summary>
    /// The register's delete. It switches the account off rather than removing it — see
    /// UsersApiController.SetActive — and the same call with true brings it back.
    /// </summary>
    public Task<ApiResponse<CreatedUser>> SetUserActiveAsync(
        Guid userId, bool isActive, CancellationToken cancellationToken = default)
        => PutAsync<CreatedUser>(
            $"api/users/{userId}/active",
            new SetUserActiveRequest { IsActive = isActive },
            cancellationToken);

    // --- Installment schemes -----------------------------------------------------
    // The terms a household can be offered. The register lists them; the configuration
    // page opens one at a time and saves the five figures back. Nothing derived travels
    // on this wire - SchemeMath computes the rate, the charge and the schedule from the
    // terms, on both pages, from the one implementation.

    public Task<ApiResponse<QueryableEntity<SchemeRow>>> GetSchemesAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        int? minMonthPeriods,
        int? maxMonthPeriods,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/installment-schemes?page={page}&pageSize={pageSize}"
            + Param("search", search)
            + Param("isActive", isActive)
            + Param("minMonthPeriods", minMonthPeriods)
            + Param("maxMonthPeriods", maxMonthPeriods);

        return GetAsync<QueryableEntity<SchemeRow>>(url, cancellationToken);
    }

    /// <summary>One scheme, for the configuration page to open. A 404 means it is gone.</summary>
    public Task<ApiResponse<SchemeRow>> GetSchemeAsync(
        int schemeId, CancellationToken cancellationToken = default)
        => GetAsync<SchemeRow>($"api/installment-schemes/{schemeId}", cancellationToken);

    public Task<ApiResponse<SavedScheme>> CreateSchemeAsync(
        SaveSchemeRequest request, CancellationToken cancellationToken = default)
        => PostAsync<SavedScheme>("api/installment-schemes", request, cancellationToken);

    public Task<ApiResponse<SavedScheme>> UpdateSchemeAsync(
        int schemeId, SaveSchemeRequest request, CancellationToken cancellationToken = default)
        => PutAsync<SavedScheme>($"api/installment-schemes/{schemeId}", request, cancellationToken);

    /// <summary>
    /// The scheme register's delete. It withdraws the scheme from offer rather than
    /// removing it - see InstallmentSchemesApiController.SetActive - and the same call
    /// with true puts it back.
    /// </summary>
    public Task<ApiResponse<SavedScheme>> SetSchemeActiveAsync(
        int schemeId, bool isActive, CancellationToken cancellationToken = default)
        => PutAsync<SavedScheme>(
            $"api/installment-schemes/{schemeId}/active",
            new SetSchemeActiveRequest { IsActive = isActive },
            cancellationToken);

    // --- Investment schemes ------------------------------------------------------
    // The terms an investor can place capital on. Same shape as the installment register
    // above and for the same reasons - nothing derived travels on this wire, because
    // InvestmentMath computes the periodic return, the per-annum profit and the payout
    // schedule from the terms, on both pages, from the one implementation.

    public Task<ApiResponse<QueryableEntity<InvestmentSchemeRow>>> GetInvestmentSchemesAsync(
        int page,
        int pageSize,
        string? search,
        bool? isActive,
        int? schemeType,
        int? minInvestmentYears,
        int? maxInvestmentYears,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/investment-schemes?page={page}&pageSize={pageSize}"
            + Param("search", search)
            + Param("isActive", isActive)
            + Param("schemeType", schemeType)
            + Param("minInvestmentYears", minInvestmentYears)
            + Param("maxInvestmentYears", maxInvestmentYears);

        return GetAsync<QueryableEntity<InvestmentSchemeRow>>(url, cancellationToken);
    }

    /// <summary>One scheme, for the configuration page to open. A 404 means it is gone.</summary>
    public Task<ApiResponse<InvestmentSchemeRow>> GetInvestmentSchemeAsync(
        Guid schemeId, CancellationToken cancellationToken = default)
        => GetAsync<InvestmentSchemeRow>($"api/investment-schemes/{schemeId}", cancellationToken);

    public Task<ApiResponse<SavedInvestmentScheme>> CreateInvestmentSchemeAsync(
        SaveInvestmentSchemeRequest request, CancellationToken cancellationToken = default)
        => PostAsync<SavedInvestmentScheme>("api/investment-schemes", request, cancellationToken);

    public Task<ApiResponse<SavedInvestmentScheme>> UpdateInvestmentSchemeAsync(
        Guid schemeId, SaveInvestmentSchemeRequest request, CancellationToken cancellationToken = default)
        => PutAsync<SavedInvestmentScheme>($"api/investment-schemes/{schemeId}", request, cancellationToken);

    /// <summary>
    /// The investment scheme register's delete. It withdraws the scheme from offer rather
    /// than removing it - see InvestmentSchemesApiController.SetActive - and the same call
    /// with true puts it back.
    /// </summary>
    public Task<ApiResponse<SavedInvestmentScheme>> SetInvestmentSchemeActiveAsync(
        Guid schemeId, bool isActive, CancellationToken cancellationToken = default)
        => PutAsync<SavedInvestmentScheme>(
            $"api/investment-schemes/{schemeId}/active",
            new SetInvestmentSchemeActiveRequest { IsActive = isActive },
            cancellationToken);

    // --- The host ----------------------------------------------------------------

    /// <summary>
    /// The version the API reports for itself. Anonymous: the landing page and the
    /// sign-in screen both print it before anybody has a session.
    /// </summary>
    /// <remarks>
    /// Components go through <see cref="ServerVersionProvider"/> rather than calling this.
    /// This client is registered transient, so it has nowhere to keep an answer; the
    /// provider is what lets every stamp the app renders share one request.
    /// </remarks>
    public Task<ApiResponse<ServerVersion>> GetServerVersionAsync(
        CancellationToken cancellationToken = default)
        => GetAsync<ServerVersion>("api/system/version", cancellationToken);

    /// <summary>
    /// One query-string parameter, or nothing at all when the value is unset. Dates go
    /// over as a plain calendar day: the filter means "this day", and letting a round-trip
    /// format attach a time zone to it is how a day drifts by one on the way to the server.
    /// </summary>
    private static string Param(string name, object? value) => value switch
    {
        null => string.Empty,
        string text when string.IsNullOrWhiteSpace(text) => string.Empty,
        string text => $"&{name}={Uri.EscapeDataString(text.Trim())}",
        DateTime date => $"&{name}={date:yyyy-MM-dd}",
        bool flag => $"&{name}={(flag ? "true" : "false")}",
        _ => $"&{name}={Uri.EscapeDataString(value.ToString() ?? string.Empty)}",
    };

    private async Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync<T>(await _http.GetAsync(url, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Unreachable<T>();
        }
    }

    private async Task<ApiResponse<T>> PostAsync<T>(
        string url, object? body, CancellationToken cancellationToken)
    {
        try
        {
            var response = body is null
                ? await _http.PostAsync(url, content: null, cancellationToken)
                : await _http.PostAsJsonAsync(url, body, JsonOptions, cancellationToken);

            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Unreachable<T>();
        }
    }

    private async Task<ApiResponse<T>> PutAsync<T>(
        string url, object body, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadAsync<T>(
                await _http.PutAsJsonAsync(url, body, JsonOptions, cancellationToken),
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Unreachable<T>();
        }
    }

    private static async Task<ApiResponse<T>> ReadAsync<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        // A 401 has an envelope of its own, but it is also the one status the caller acts
        // on structurally rather than by message, so short-circuit it before parsing.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new ApiResponse<T>
            {
                Code = (int)HttpStatusCode.Unauthorized,
                Message = "Your session has ended. Please sign in again.",
            };
        }

        try
        {
            var parsed = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(
                JsonOptions, cancellationToken);

            if (parsed is not null)
            {
                return parsed;
            }
        }
        catch (JsonException)
        {
            // Fall through: the body was not the envelope (a proxy error page, a 429 with
            // no body). Report the status rather than surfacing a parser error.
        }

        return new ApiResponse<T>
        {
            Code = (int)response.StatusCode,
            Message = response.StatusCode == HttpStatusCode.TooManyRequests
                ? "Too many attempts. Wait a minute and try again."
                : "The server returned an unexpected response.",
        };
    }

    private static ApiResponse<T> Unreachable<T>() => new()
    {
        Code = 0,
        Message = "Cannot reach the fund API. Check that it is running, then try again.",
    };
}
