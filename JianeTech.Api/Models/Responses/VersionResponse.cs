namespace JianeTech.Api.Models.Responses;

/// <summary>
/// What <c>GET /api/system/version</c> answers with. Note what is absent: no environment
/// name, no machine name, no framework or runtime version. The endpoint is anonymous, so
/// this carries the one fact the console prints and nothing a stranger could use to
/// work out what the host is built on.
/// </summary>
public class VersionResponse
{
    public required string Version { get; set; }
}
