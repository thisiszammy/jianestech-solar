using JianeTech.Client;
using JianeTech.Client.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// ApiBaseUrl points at JianeTech.Api (wwwroot/appsettings*.json); falls back to this
// app's own origin.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress;

// CredentialsHandler is what lets the HttpOnly auth cookies ride along on a
// cross-origin fetch. Without it every call arrives anonymous.
builder.Services.AddScoped<CredentialsHandler>();

builder.Services
    .AddHttpClient<FundApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
    .AddHttpMessageHandler<CredentialsHandler>();

// The server is the only authority on whether a session exists — this app cannot
// read its own token, so it asks.
builder.Services.AddScoped<CookieAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<CookieAuthenticationStateProvider>());

builder.Services.AddAuthorizationCore();

// Scoped for the same reason the state provider is: it caches one answer from the API
// for the life of the app, so the landing page and the sign-in screen a visitor goes to
// next share a single request.
builder.Services.AddScoped<ServerVersionProvider>();

await builder.Build().RunAsync();

/// <summary>
/// Merged with the compiler-generated Program class from the top-level statements above,
/// the same way <c>JianeTech.Api</c>'s is, so each half keeps its version in the same
/// place and a release bumps the same line in both.
/// </summary>
/// <remarks>
/// Internal, where the API's is public, and that is deliberate. A Debug build of
/// <c>JianeTech.Api</c> references this assembly, and a second public <c>Program</c> in
/// the global namespace would collide with the host's own. Nothing outside this project
/// reads it anyway: the API reports its own version, and <c>VersionStamp</c> prints both.
/// </remarks>
internal partial class Program
{
    /// <summary>Display version — bump manually on each release.</summary>
    public const string Version = "0.2.0";
}
