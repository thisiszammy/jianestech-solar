using JianeTech.Data.Enums;
using JianeTech.Services.Interfaces;

namespace JianeTech.Api.Data;

/// <summary>
/// Seeds the first administrator if — and only if — no account exists yet.
/// </summary>
/// <remarks>
/// The schema itself is <b>not</b> managed here. This project is database-first: the
/// tables live in the existing <c>JianeTechSolar</c> database and the entities are
/// regenerated from them by EF Core Power Tools (see
/// <c>JianeTech.Data/Entities/efpt.config.json</c>). There are no migrations to apply, so
/// this never calls <c>Database.Migrate()</c> — doing so would try to take ownership of a
/// schema it does not own.
/// </remarks>
public static class DatabaseInitializer
{
    /// <summary>
    /// Seeding goes through <see cref="IUserService"/> rather than straight to the context
    /// on purpose: the first account is a real registration and earns a real
    /// <c>UserRegistered</c> audit row, so the trail is complete from row one.
    /// </summary>
    public static async Task InitializeAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));

        var userService = provider.GetRequiredService<IUserService>();
        if (await userService.AnyUsersExistAsync(CancellationToken.None))
        {
            return;
        }

        var configuration = provider.GetRequiredService<IConfiguration>();
        var username = configuration["Seed:AdminUsername"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            // Not an error: a deployment that provisions its administrator some other way
            // should not be forced to put a password in configuration.
            logger.LogWarning(
                "No accounts exist and Seed:AdminUsername / Seed:AdminPassword are not configured. "
                + "Skipping seed - no one can sign in until an account is created.");
            return;
        }

        var userId = await userService.RegisterUserAsync(
            configuration["Seed:AdminFirstName"] ?? "Platform",
            configuration["Seed:AdminLastName"] ?? "Administrator",
            username,
            configuration["Seed:AdminEmail"] ?? $"{username}@jianestech.local",
            configuration["Seed:AdminPhoneNumber"] ?? string.Empty,
            password,
            UserTypeEnum.SystemAdmin,
            createdBy: null,
            CancellationToken.None);

        logger.LogInformation(
            "Seeded the first administrator account {Username} ({UserId}).", username, userId);
    }
}
