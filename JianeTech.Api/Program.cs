using System.Text;
using System.Threading.RateLimiting;
using JianeTech.Api.Authentication;
using JianeTech.Api.Data;
using JianeTech.Api.Middleware;
using JianeTech.Api.Models.Responses;
using JianeTech.Data;
using JianeTech.Data.Entities;
using JianeTech.Data.Interfaces;
using JianeTech.Data.Interfaces.Database;
using JianeTech.Data.Repositories.Database;
using JianeTech.Services;
using JianeTech.Services.Interfaces;
using JianeTech.Services.Services;
using JianeTech.Services.Validators;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

// sample credentials
// cmendoza
// 2rnc2U3Wec4C7EHs

DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

var seqUrl = builder.Configuration["Seq:Url"];

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration)
      .Enrich.FromLogContext()
      .Enrich.WithProperty("Application", "[JIANESTECH-SOLAR]");

#if !DEBUG
if (!string.IsNullOrEmpty(seqUrl))
    {
        lc.WriteTo.Seq(seqUrl);
    }
#else
    lc.WriteTo.Console();
#endif
    
});

// ---------------------------------------------------------------------------
// Data layer
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found. Set ConnectionStrings__DefaultConnection in .env.");

builder.Services.AddDbContext<DatabaseContext>(options => options.UseSqlServer(connectionString));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAccessRefreshTokenRepository, AccessRefreshTokenRepository>();
builder.Services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
builder.Services.AddScoped<IUserActivationTokenRepository, UserActivationTokenRepository>();
builder.Services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
builder.Services.AddScoped<IInstallmentSchemeRepository, InstallmentSchemeRepository>();
builder.Services.AddScoped<IInvestmentSchemeRepository, InvestmentSchemeRepository>();

// ---------------------------------------------------------------------------
// Service layer
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddSingleton<IDateTimeService, DateTimeService>();

// Validators are stateless by contract, hence singletons shared across every request.
builder.Services.AddSingleton<AuthenticationValidator>();
builder.Services.AddSingleton<UserValidator>();
builder.Services.AddSingleton<AccountActivationValidator>();
builder.Services.AddSingleton<PasswordResetValidator>();
builder.Services.AddSingleton<InstallmentSchemeValidator>();
builder.Services.AddSingleton<InvestmentSchemeValidator>();

builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IActivityLogService, ActivityLogService>();
builder.Services.AddScoped<IAccountActivationService, AccountActivationService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IInstallmentSchemeService, InstallmentSchemeService>();
builder.Services.AddScoped<IInvestmentSchemeService, InvestmentSchemeService>();

// The one service that talks to a third party. Scoped rather than singleton so it reads
// its configuration per request - the Brevo keys arrive from .env, and a console started
// before they were filled in should pick them up on the next restart, not cache an empty
// key for the life of the process.
builder.Services.AddScoped<IEmailService, BrevoEmailService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<IClientContextAccessor, HttpClientContextAccessor>();

// ---------------------------------------------------------------------------
// CORS — only for a client served from somewhere other than this host
// ---------------------------------------------------------------------------
// A Debug build serves JianeTech.Client itself (see the pipeline below), so the normal
// development case is same-origin and needs no CORS at all. The allow-list exists for the
// topologies where it is not: the client's own dev server on its own port for hot reload,
// and a Release build, which serves no console at all and so always has one elsewhere.
//
// AllowCredentials is what lets the browser attach the HttpOnly auth cookies to a
// cross-origin fetch. It is incompatible with AllowAnyOrigin by specification, so origins
// are listed exactly — see Cors:AllowedOrigins. An empty list means no CORS surface at
// all, which is the correct, tighter posture when nothing is cross-origin.
const string ClientCorsPolicy = "Client";
var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Select(entry =>
    {
        if (!Uri.TryCreate(entry, UriKind.Absolute, out var origin)
            || (origin.Scheme != Uri.UriSchemeHttp && origin.Scheme != Uri.UriSchemeHttps)
            || origin.PathAndQuery != "/")
        {
            throw new InvalidOperationException(
                $"Cors:AllowedOrigins entry '{entry}' is not an origin (expected scheme://host[:port]).");
        }

        // Exactly what the browser puts in its Origin header: no trailing slash.
        return origin.GetLeftPart(UriPartial.Authority);
    })
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
        options.AddPolicy(ClientCorsPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
}

// ---------------------------------------------------------------------------
// Authentication
// ---------------------------------------------------------------------------
var jwtIssuer = builder.Configuration["Jwt:Issuers"]
    ?? throw new InvalidOperationException("Jwt:Issuers is not configured. Set Jwt__Issuers in .env.");
var jwtAudience = builder.Configuration["Jwt:Audience"]
    ?? throw new InvalidOperationException("Jwt:Audience is not configured. Set Jwt__Audience in .env.");
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured. Set Jwt__Key in .env.");

// HMAC-SHA256 needs at least 256 bits of key material; a shorter secret fails at signing
// time with an opaque error, so reject it here where the message can say what to fix.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 bytes (256 bits). Set a longer Jwt__Key in .env.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            // A small tolerance rather than zero: nbf is the mint-time "now" truncated to
            // whole seconds, so a sub-second clock wobble across a second boundary would
            // reject a token issued milliseconds earlier — which surfaces as "the refresh
            // succeeded but the user was signed out anyway".
            ClockSkew = TimeSpan.FromMinutes(1),

            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),

            // Stated rather than left to the default, which happens to be the same
            // value. [Authorize(Roles = ...)] reads whichever claim this names, so
            // the minting side and the checking side are pinned to one constant —
            // a silent disagreement here would let every role check pass nobody.
            RoleClaimType = JwtTokenGenerator.RoleClaimType,
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // 1. A token minted by AccessTokenRefreshMiddleware earlier in this same
                //    request wins outright.
                if (context.HttpContext.Items.TryGetValue(
                        AccessTokenRefreshMiddleware.RefreshedAccessTokenKey, out var freshObject)
                    && freshObject is string freshToken
                    && !string.IsNullOrEmpty(freshToken))
                {
                    context.Token = freshToken;
                    return Task.CompletedTask;
                }

                // 2. A programmatic caller sends its own bearer. Leaving context.Token null
                //    lets the handler's own header reader take it.
                var header = context.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrEmpty(header)
                    && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    return Task.CompletedTask;
                }

                // 3. The browser client has no header to send — its token is in the
                //    HttpOnly jt_atk cookie.
                if (context.Request.Cookies.TryGetValue(
                        AuthenticationCookies.AccessTokenName, out var cookieToken)
                    && !string.IsNullOrEmpty(cookieToken))
                {
                    context.Token = cookieToken;
                }

                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                // This host serves an API only — the UI is a separate Blazor origin — so a
                // challenge is answered with a machine-readable 401, never a redirect.
                context.HandleResponse();

                // Drop the cookies so the client stops presenting a bearer that has just
                // been rejected... unless AccessTokenRefreshMiddleware rotated them on this
                // very request. IResponseCookies.Delete strips matching earlier Appends off
                // the same response, so clearing here would erase the freshly minted
                // cookies and turn a recoverable challenge into a permanent sign-out.
                var middlewareRefreshed =
                    context.HttpContext.Items.TryGetValue(
                        AccessTokenRefreshMiddleware.RefreshedItemKey, out var refreshed)
                    && refreshed is true;

                if (!middlewareRefreshed)
                {
                    context.Response.Cookies.Delete(
                        AuthenticationCookies.AccessTokenName, AuthenticationCookies.ClearOptions);
                    context.Response.Cookies.Delete(
                        AuthenticationCookies.RefreshTokenName, AuthenticationCookies.ClearOptions);
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return context.Response.WriteAsJsonAsync(new ApiResponse
                {
                    Code = StatusCodes.Status401Unauthorized,
                    Message = "Your session has ended. Please sign in again.",
                    Data = null,
                });
            },

            OnForbidden = context =>
            {
                // Authenticated but not permitted. Same envelope as every other response.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return context.Response.WriteAsJsonAsync(new ApiResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Message = "You do not have permission to perform this action.",
                    Data = null,
                });
            },
        };
    });

builder.Services.AddAuthorization();

// ---------------------------------------------------------------------------
// Rate limiting — partitioned per client IP
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Credential submission: tight enough to make online guessing impractical.
    options.AddPolicy("auth-strict", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // Session upkeep: a signed-in tab legitimately refreshes and polls.
    options.AddPolicy("auth-general", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

builder.Services.AddControllers();

var app = builder.Build();

// Said out loud because neither outcome announces itself otherwise — see the note on
// seqUrl above. It is the first thing written, so "is it even trying?" is answered before
// anything else in the log is read. Disabled is a warning rather than information: a
// configured capability that is not running is worth one line on every start, and a
// deployment that genuinely wants no Seq can afford it.
if (string.IsNullOrEmpty(seqUrl))
{
    app.Logger.LogWarning(
        "Seq sink disabled — no Seq:Url configured. Set Seq:Url in appsettings.json, or Seq__Url in .env.");
}
else
{
    app.Logger.LogInformation("Seq sink enabled -> {SeqUrl}", seqUrl);
}

app.UseSerilogRequestLogging();

#if DEBUG
if (app.Environment.IsDevelopment())
{
    // Lets the browser debugger step through the client's C#. Only a Debug build has a
    // client to step through — see the hosting section below.
    app.UseWebAssemblyDebugging();
}
#endif

app.UseHttpsRedirection();

// Baseline security headers on everything this host returns — the console shell, the
// API, and static files alike. SAMEORIGIN rather than DENY leaves room for same-origin
// print previews later.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "SAMEORIGIN";
    headers["Referrer-Policy"] = "same-origin";
    await next();
});

// ---------------------------------------------------------------------------
// Serve JianeTech.Client from this host — Debug builds only
// ---------------------------------------------------------------------------
// The console is a Blazor WebAssembly app referenced by this project, so a Debug build
// runs the whole application: _framework/* comes from the referenced client, everything
// else from its wwwroot, and any unmatched non-API path falls back to index.html so the
// client's own router takes over.
//
// Serving it here also makes the auth cookies first-party, which is the topology the
// cookie design actually wants — nothing is cross-origin, so nothing depends on the CORS
// allow-list agreeing with the cookie's SameSite setting.
//
// A Release build is an API and nothing else. The ProjectReference to JianeTech.Client is
// itself conditioned on Debug (see JianeTech.Api.csproj), so there are no client assets in
// the published output to serve — guarding the middleware without it would still leave the
// whole console sitting in wwwroot, reachable by direct path. The console is then deployed
// on its own origin, which has to be listed in Cors:AllowedOrigins for its auth cookies to
// survive the round trip.
#if DEBUG
app.UseBlazorFrameworkFiles();

// index.html and the plain-named assets it links (css/*, favicon) carry no fingerprint,
// so a browser given only an ETag applies heuristic freshness and can serve a stale
// stylesheet over new markup — or an import map pointing at module URLs that no longer
// exist. no-cache keeps the cached copy but forces revalidation (a 304 when nothing
// changed). The fingerprinted routes the import map points at are served by
// MapStaticAssets with its own headers.
var plainAssetOptions = new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
};
app.UseStaticFiles(plainAssetOptions);
#endif

app.UseRouting();

// Only in the pipeline when a cross-origin client is actually configured. Sits before the
// auth pipeline so preflights short-circuit early and the CORS headers also ride on the
// 401s the client's session handling depends on.
if (allowedOrigins.Length > 0)
{
    app.UseCors(ClientCorsPolicy);
}

// Must sit before UseAuthentication so a silently refreshed token is already in place by
// the time the bearer handler looks for one.
app.UseMiddleware<AccessTokenRefreshMiddleware>();

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

#if DEBUG
// The fingerprinted asset routes the client's import map points at (dotnet.js and the
// framework modules) exist only as static-asset endpoints until publish materializes
// them — UseStaticFiles alone would 404 them on a dev run.
app.MapStaticAssets();

// An unknown /api/* route stays a 404 rather than falling through to the client shell,
// which would answer a missing endpoint with 200 and a page of HTML. A Release build maps
// no shell to fall through to, so an unmatched route already answers 404 on its own.
app.MapFallback("/api/{**slug}", () => Results.NotFound());

// Everything else is a client route — hand it the shell and let the Blazor router decide.
app.MapFallbackToFile("index.html", plainAssetOptions);
#endif

app.Run();

/// <summary>
/// Merged with the compiler-generated Program class from the top-level statements above so
/// the app can expose metadata (and so tests can reference the entry point).
/// </summary>
public partial class Program
{
    /// <summary>Display version — bump manually on each release.</summary>
    public const string Version = "0.2.2";
}
