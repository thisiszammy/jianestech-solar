namespace JianeTech.Api.Models.Requests;

/// <summary>
/// Query-string filters for the account register. Every field is optional; the service
/// clamps out-of-range paging values rather than rejecting them.
/// </summary>
public class ListUsersRequest
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    /// <summary>Matches name, username, email, or phone number.</summary>
    public string? Search { get; set; }

    /// <summary>A <c>UserTypeEnum</c> value to filter to.</summary>
    public int? UserType { get; set; }

    public bool? IsActive { get; set; }

    public bool? IsLocked { get; set; }

    /// <summary>Invited and still waiting on their link. See <c>UserDTO.IsPending</c>.</summary>
    public bool? IsPending { get; set; }
}
