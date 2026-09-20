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

await builder.Build().RunAsync();
