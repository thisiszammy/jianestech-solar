namespace JianeTech.Client.Auth;

/// <summary>
/// What asking the API for its version came to.
/// </summary>
/// <param name="Version">The version the API reported, or null if it reported none.</param>
/// <param name="Reached">
/// Whether the API answered at all. False is a network fault. True with no version is an
/// answer that was not the one asked for — a 429, or an older API deployed without the
/// endpoint — and the stamp words the two differently, because "unreachable" would be
/// untrue of a server that has just replied.
/// </param>
public sealed record ServerVersionReading(string? Version, bool Reached);

/// <summary>
/// The API's version, asked for once and shared.
/// </summary>
/// <remarks>
/// <para>
/// The console's own version is a constant it was compiled with. The server's cannot be
/// known from here — a Release deployment builds and ships the two halves separately,
/// which is exactly when it matters which one is answering — so it is asked for, from
/// <c>GET /api/system/version</c>.
/// </para>
/// <para>
/// Same shape as <see cref="CookieAuthenticationStateProvider"/>, for the same reason:
/// the <em>Task</em> is cached rather than its result, so a stamp rendered while the
/// request is still in flight joins it instead of firing a second one, and the sign-in
/// screen a visitor reaches from the landing page reuses the answer outright.
/// </para>
/// </remarks>
public class ServerVersionProvider
{
    /// <summary>
    /// Longer than any version this fund will ship ("10.12.3-beta.4" is 14), and short
    /// enough that a malformed answer cannot push a footer off the side of a phone.
    /// </summary>
    private const int MaxVersionLength = 32;

    private readonly FundApiClient _api;

    private Task<ServerVersionReading>? _reading;

    public ServerVersionProvider(FundApiClient api)
    {
        _api = api;
    }

    public Task<ServerVersionReading> GetAsync()
    {
        // Only an answer worth keeping is kept. A reading that came back without a
        // version is dropped, so the next page to render a stamp asks again — an API
        // that was restarting when the landing page loaded should not still read as
        // unreachable on the sign-in screen a minute later. One still in flight is
        // left alone: that is the request everybody is supposed to be sharing.
        if (_reading is { IsCompletedSuccessfully: true } finished && finished.Result.Version is null)
        {
            _reading = null;
        }

        return _reading ??= ReadAsync();
    }

    private async Task<ServerVersionReading> ReadAsync()
    {
        try
        {
            var response = await _api.GetServerVersionAsync();

            if (response.Code == 200
                && response.Data?.Version?.Trim() is { Length: > 0 and <= MaxVersionLength } version)
            {
                return new ServerVersionReading(version, Reached: true);
            }

            // FundApiClient reports a request that never got an answer as Code 0.
            return new ServerVersionReading(null, Reached: response.Code != 0);
        }
        catch (Exception)
        {
            // Deliberately everything. FundApiClient already turns the failures it
            // expects into an envelope, so whatever arrives here is one it did not —
            // and a line of fine print must never be the reason a page fails to load.
            // Something answered, or the client would have said otherwise.
            return new ServerVersionReading(null, Reached: true);
        }
    }
}
