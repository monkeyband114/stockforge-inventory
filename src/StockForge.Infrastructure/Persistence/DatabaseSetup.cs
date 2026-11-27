using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Infrastructure.Seeding;

namespace StockForge.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>Apply migrations when the process starts (development convenience; production uses <c>--migrate</c>).</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>Sync roles/permissions and the super-admin account on start.</summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>
    /// When set, the migrator grants LOGIN to the <c>stockforge_app</c> role with this password. Lets local
    /// development and tests provision the least-privileged runtime role without manual SQL.
    /// </summary>
    public string? AppRolePassword { get; set; }
}

internal static class AppDbContextOptions
{
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder builder, string connectionString) =>
        builder
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(2), null);
                npgsql.MigrationsHistoryTable("__ef_migrations_history");
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            })
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    public static AppDbContext CreateStandalone(string connectionString)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        Configure(builder, connectionString);
        return new AppDbContext(builder.Options, new ScopeContext(), new DomainEventBuffer(new NullDomainEventDispatcher()));
    }
}

/// <summary>Used by <c>dotnet ef</c>; connects with the owner role from <c>STOCKFORGE_MIGRATIONS_CONNECTION</c>.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        AppDbContextOptions.CreateStandalone(
            Environment.GetEnvironmentVariable("STOCKFORGE_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=stockforge;Username=postgres;Password=postgres");
}

/// <summary>
/// Applies migrations with the owner connection (<c>ConnectionStrings:Migrations</c>) and seeds reference
/// data with the runtime connection. The runtime role is not the table owner, so RLS applies to it.
/// </summary>
public sealed class DatabaseInitializer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    IOptions<DatabaseOptions> options,
    IOptions<SeedOptions> seedOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Migrations")
                               ?? configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("ConnectionStrings:Migrations is not configured.");
        await using var db = AppDbContextOptions.CreateStandalone(connectionString);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        logger.LogInformation("Applying {Count} pending migration(s)", pending.Count);
        await db.Database.MigrateAsync(cancellationToken);

        if (options.Value.AppRolePassword is { Length: > 0 } password)
        {
            // format(%L) quotes the literal safely; DDL cannot take bind parameters.
            var statement = (await db.Database
                    .SqlQuery<string>($"SELECT format('ALTER ROLE stockforge_app WITH LOGIN PASSWORD %L', {password}) AS \"Value\"")
                    .ToListAsync(cancellationToken))
                .Single();
            await db.Database.ExecuteSqlRawAsync(statement, cancellationToken);
            logger.LogInformation("Provisioned login for the stockforge_app runtime role");
        }
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(cancellationToken);
        }

        if (seedOptions.Value.DemoData)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync(cancellationToken);
        }
    }
}
