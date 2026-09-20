using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace JianeTech.Client.Auth;

/// <summary>
/// Attaches the auth cookies to every outgoing request.
/// </summary>
/// <remarks>
/// The browser will not send cookies on a cross-origin fetch unless the request opts in,
/// and this app is a different origin from the API. This is one of three halves that must
/// agree — the others are the API's CORS <c>AllowCredentials()</c> and the cookies'
/// <c>SameSite=None; Secure</c>. Remove any one and the calls still succeed, they just
/// arrive unauthenticated, which looks like a broken session rather than a missing header.
/// </remarks>
public class CredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
