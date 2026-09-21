using System.Text.Json.Serialization;

namespace JianeTech.Client.Auth.Models;

/// <summary>
/// Mirrors <c>JianeTech.Api.Models.Responses.ApiResponse</c>. Every endpoint returns this
/// shape, success or failure, so one deserialization covers both.
/// </summary>
public class ApiResponse<T>
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }
}

/// <summary>
/// Note what is absent: no access token. It lives only in the HttpOnly <c>jt_atk</c>
/// cookie, so this app can never read it — which is the point.
/// </summary>
public class AuthenticationResponse
{
    public DateTime AccessTokenExpiresOn { get; set; }

    public AuthenticatedUser? User { get; set; }
}

public class AuthenticatedUser
{
    public Guid UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public int UserType { get; set; }

    public string UserTypeLabel { get; set; } = string.Empty;

    public DateTime? LastSignInOn { get; set; }

    [JsonIgnore]
    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>Two letters for the sidebar mark; falls back to the username.</summary>
    [JsonIgnore]
    public string Initials
    {
        get
        {
            var first = FirstName.Length > 0 ? FirstName[0] : ' ';
            var last = LastName.Length > 0 ? LastName[0] : ' ';
            var initials = $"{first}{last}".Trim();
            return initials.Length > 0
                ? initials.ToUpperInvariant()
                : (Username.Length > 0 ? Username[..1].ToUpperInvariant() : "?");
        }
    }
}

public class AuthenticateRequest
{
    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
}

public class QueryableEntity<T>
{
    public List<T> Items { get; set; } = [];

    public int TotalRows { get; set; }
}

public class ActivityLogEntry
{
    public Guid ActivityLogId { get; set; }

    public Guid BatchId { get; set; }

    public int ExecutionOrder { get; set; }

    public int Activity { get; set; }

    public string ActivityLabel { get; set; } = string.Empty;

    /// <summary>success / failed / blocked — drives the row's status dot.</summary>
    public string Status { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public string IpAddress { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public DateTime ExecutedOn { get; set; }

    public Guid? ExecutedBy { get; set; }

    public string? ExecutedByName { get; set; }

    public string? ExecutedByEmail { get; set; }

    public int? ReferenceType { get; set; }

    public Guid? Reference { get; set; }
}

public class DashboardSnapshot
{
    public DashboardStats Stats { get; set; } = new();

    public List<SignInBucket> SignInActivity { get; set; } = [];

    public List<AccountStatusBucket> AccountStatus { get; set; } = [];

    public List<ActivityLogEntry> RecentActivity { get; set; } = [];

    public DateTime GeneratedOn { get; set; }
}

public class DashboardStats
{
    public int ActiveAccounts { get; set; }

    public int LockedAccounts { get; set; }

    public int LiveSessions { get; set; }

    public int SignIns30d { get; set; }

    public int FailedSignIns30d { get; set; }

    public int TotalAuditedEvents { get; set; }
}

public class SignInBucket
{
    public DateTime Day { get; set; }

    public int Success { get; set; }

    public int Failed { get; set; }
}

public class AccountStatusBucket
{
    public string Label { get; set; } = string.Empty;

    /// <summary>success / warn / danger / neutral — the palette key, not a colour.</summary>
    public string Tone { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>
/// One row of the account register. Mirrors <c>JianeTech.Services.DTOs.UserDTO</c> —
/// and, like it, carries no credential material.
/// </summary>
public class UserRow
{
    public Guid UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string PhoneNumber { get; set; } = string.Empty;

    public int UserType { get; set; }

    public string UserTypeLabel { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public bool IsLocked { get; set; }

    /// <summary>Invited and still waiting on their link — mirrors <c>UserDTO.IsPending</c>.</summary>
    public bool IsPending { get; set; }

    public DateTime? LockedUntil { get; set; }

    /// <summary>Active / Inactive / Locked — printed beside its dot, never colour alone.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>success / warn / danger — the palette key, not a colour.</summary>
    public string StatusTone { get; set; } = string.Empty;

    public DateTime? LastSignInOn { get; set; }

    public DateTime? CreatedOn { get; set; }

    public string? CreatedByName { get; set; }

    [JsonIgnore]
    public string FullName => $"{FirstName} {LastName}".Trim() is { Length: > 0 } name
        ? name
        : Username;
}

/// <summary>One entry of the activity filter, worded by the server.</summary>
public class ActivityOption
{
    public int Activity { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;
}

/// <summary>
/// The invite-a-user form on the wire. No acting-user id: who is issuing the
/// invitation is read from the session cookie server-side, never sent from here.
/// And no password — the invitee chooses their own from the link this sends, so
/// none is ever typed by one person on behalf of another.
/// </summary>
public class InviteUserRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public int UserType { get; set; }
}

/// <summary>
/// The public registration form on the wire.
/// </summary>
/// <remarks>
/// <see cref="InviteUserRequest"/> minus the role. A visitor does not choose what kind
/// of account they get — the server fixes it — and a UserType property here would be an
/// invitation to post one. What is added is the interest, which is not a role either:
/// it is what the person said they came for, and it ends up in the account's audit row
/// rather than in any column.
/// </remarks>
public class RegisterAccountRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    /// <summary>A <c>RegistrationInterestEnum</c> value; 0 is "not stated".</summary>
    public int Interest { get; set; }
}

/// <summary>
/// What an invitation produced. The account is created before the mail is
/// attempted, so these two can disagree — see <c>InvitationResultDTO</c>.
/// </summary>
public class InvitationResult
{
    public Guid UserId { get; set; }

    public bool EmailSent { get; set; }

    public string? EmailError { get; set; }
}

/// <summary>
/// Who a one-time link belongs to. One type for both the invitation and the
/// recovery link: the console does exactly the same thing with each — greets
/// the reader and prints the rail above the password fields — and the two
/// server DTOs are the same shape for the same reason.
/// </summary>
public class AccountLink
{
    public string FirstName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public DateTime ExpiresOn { get; set; }
}

/// <summary>The password chosen at the end of a link. The token travels in the route.</summary>
public class SetPasswordRequest
{
    public string Password { get; set; } = string.Empty;
}

/// <summary>The forgot-password form. A username or an email address; the server takes either.</summary>
public class RequestPasswordResetRequest
{
    public string Identifier { get; set; } = string.Empty;
}

public class CreatedUser
{
    public Guid UserId { get; set; }
}

/// <summary>One entry of the role picker, worded by the server.</summary>
public class RoleOption
{
    public int UserType { get; set; }

    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// One audited operation and its steps. Mirrors
/// <c>JianeTech.Services.DTOs.ActivityLogBatchDTO</c>: the fields every step of a batch
/// shares sit here, and what differs per step sits in <see cref="Items"/>.
/// </summary>
public class ActivityLogBatch
{
    public Guid BatchId { get; set; }

    public DateTime ExecutedOn { get; set; }

    public Guid? ExecutedBy { get; set; }

    public string? ExecutedByName { get; set; }

    public string? ExecutedByEmail { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    public List<ActivityLogEntry> Items { get; set; } = [];

    /// <summary>The tone the operation as a whole reads as: its worst step decides.</summary>
    [JsonIgnore]
    public string Status =>
        Items.Any(i => i.Status == "blocked") ? "blocked"
        : Items.Any(i => i.Status == "failed") ? "failed"
        : "success";
}

/// <summary>The edit form on the wire. No password, no acting-user id.</summary>
public class UpdateUserRequest
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public int UserType { get; set; }
}

/// <summary>
/// The register's on/off switch. States the wanted state rather than an action, so a
/// double-click cannot toggle twice.
/// </summary>
public class SetUserActiveRequest
{
    public bool IsActive { get; set; }
}

/// <summary>
/// One installment scheme. Mirrors <c>JianeTech.Services.DTOs.InstallmentSchemeDTO</c>:
/// the five stored terms and nothing derived from them. Every figure the console shows
/// beyond these — the finance charge, the rate, the instalment as a share of the
/// guideline bill — is computed by <c>SchemeMath</c>, because the configuration page has
/// to compute them on every keystroke anyway and two implementations of one calculation
/// could disagree.
/// </summary>
public class SchemeRow
{
    public int SchemeId { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>The cash price of the installation.</summary>
    public decimal PrincipalAmount { get; set; }

    public decimal DownPayment { get; set; }

    public decimal PeriodicPayment { get; set; }

    public int MonthPeriods { get; set; }

    /// <summary>
    /// The monthly electricity bill this scheme is recommended up to — advisory, and the
    /// figure that decides which households are offered it. Nothing is paid out of it.
    /// </summary>
    public decimal ReferenceBillAmount { get; set; }

    public bool IsActive { get; set; }

    /// <summary>On offer / Withdrawn — printed beside its dot, never colour alone.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>success / warn — the palette key, not a colour.</summary>
    public string StatusTone { get; set; } = string.Empty;

    public DateTime? CreatedOn { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedOn { get; set; }

    public string? UpdatedByName { get; set; }
}

/// <summary>
/// The scheme configuration form on the wire — the same five terms whether the scheme is
/// new or being rewritten, because it is the same form. No acting-user id, and no
/// <c>IsActive</c>: whether a scheme is on offer is a separate act with its own audit row.
/// </summary>
public class SaveSchemeRequest
{
    public string Description { get; set; } = string.Empty;

    public decimal PrincipalAmount { get; set; }

    public decimal DownPayment { get; set; }

    public decimal PeriodicPayment { get; set; }

    public int MonthPeriods { get; set; }

    public decimal ReferenceBillAmount { get; set; }
}

/// <summary>
/// The scheme register's on/off switch. States the wanted state rather than an action, so
/// a double-click cannot toggle twice.
/// </summary>
public class SetSchemeActiveRequest
{
    public bool IsActive { get; set; }
}

public class SavedScheme
{
    public int SchemeId { get; set; }
}

/// <summary>
/// One investment scheme. Mirrors <c>JianeTech.Services.DTOs.InvestmentSchemeDTO</c>: the
/// six stored terms and nothing derived from them. Every figure the console shows beyond
/// these — the periodic return, the per-annum profit, the payout schedule — is computed by
/// <c>InvestmentMath</c>, because the configuration page has to compute them on every
/// keystroke anyway and two implementations of one calculation could disagree.
/// </summary>
public class InvestmentSchemeRow
{
    public Guid SchemeId { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>0 immediate, 1 deferred — see <c>SchemeStructure</c>, which mirrors it.</summary>
    public int SchemeType { get; set; }

    /// <summary>"Immediate" / "Deferred", worded by the server so the audit trail agrees.</summary>
    public string SchemeTypeLabel { get; set; } = string.Empty;

    /// <summary>Where in the term the installation lands, said in words.</summary>
    public string SchemeTypeTiming { get; set; } = string.Empty;

    /// <summary>The floor under a placement on these terms.</summary>
    public decimal MinimumInvestment { get; set; }

    /// <summary>
    /// The profit interest per annum as a percentage — 12.5 is 12.5% a year, not 0.125.
    /// </summary>
    public decimal IncomeInterest { get; set; }

    /// <summary>Months at the head of the term that pay nothing.</summary>
    public int GracePeriodMonths { get; set; }

    public int InvestmentYears { get; set; }

    /// <summary>What the installation the investor receives at no cost is worth.</summary>
    public decimal FreeInstallationAmt { get; set; }

    public bool IsActive { get; set; }

    /// <summary>On offer / Withdrawn — printed beside its dot, never colour alone.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>success / warn — the palette key, not a colour.</summary>
    public string StatusTone { get; set; } = string.Empty;

    public DateTime? CreatedOn { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime? UpdatedOn { get; set; }

    public string? UpdatedByName { get; set; }
}

/// <summary>
/// The investment scheme configuration form on the wire — the same six terms whether the
/// scheme is new or being rewritten, because it is the same form. No acting-user id, and
/// no <c>IsActive</c>: whether a scheme is on offer is a separate act with its own audit
/// row. The placement the page models the figures at does not travel either; it is not
/// part of the scheme.
/// </summary>
public class SaveInvestmentSchemeRequest
{
    public string Description { get; set; } = string.Empty;

    public int SchemeType { get; set; }

    public decimal MinimumInvestment { get; set; }

    public decimal IncomeInterest { get; set; }

    public int GracePeriodMonths { get; set; }

    public int InvestmentYears { get; set; }

    public decimal FreeInstallationAmt { get; set; }
}

/// <summary>
/// The investment scheme register's on/off switch. States the wanted state rather than an
/// action, so a double-click cannot toggle twice.
/// </summary>
public class SetInvestmentSchemeActiveRequest
{
    public bool IsActive { get; set; }
}

public class SavedInvestmentScheme
{
    public Guid SchemeId { get; set; }
}

/// <summary>
/// What the API says it is running. Mirrors
/// <c>JianeTech.Api.Models.Responses.VersionResponse</c>, and like it carries one fact.
/// The console's own version never travels: it is a constant this app was compiled
/// with, and <c>Program.Version</c> is where it lives.
/// </summary>
public class ServerVersion
{
    public string Version { get; set; } = string.Empty;
}
