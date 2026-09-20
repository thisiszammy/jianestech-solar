namespace JianeTech.Api.Models.Responses;

/// <summary>
/// The single response shape every endpoint returns, success or failure.
/// <see cref="Code"/> always mirrors the HTTP status the action returned.
/// </summary>
public class ApiResponse
{
    public int Code { get; set; }

    public string Message { get; set; } = string.Empty;

    /// <summary>The success payload, or null on every error path.</summary>
    public object? Data { get; set; }
}
