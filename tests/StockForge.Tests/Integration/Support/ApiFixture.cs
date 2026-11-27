using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StockForge.Application.Abstractions;
using StockForge.Infrastructure.Jobs;
using StockForge.Worker;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(StockForge.Tests.Integration.Support.ApiFixture))]

namespace StockForge.Tests.Integration.Support;

/// <summary>
/// One PostgreSQL database and one API host for the whole test run. Tests isolate themselves by creating
/// their own organizations, which is exactly the isolation the platform must guarantee anyway.
/// <para>
/// Set <c>STOCKFORGE_TEST_POSTGRES</c> to an owner connection string to use an existing server (a fresh
/// database is created and dropped); otherwise a PostgreSQL 18 container is started with Testcontainers.
/// </para>
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string AppRolePassword = "stockforge_app_dev";

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;
    private string? _databaseName;

    public string OwnerConnectionString { get; private set; } = string.Empty;
    public string AppConnectionString { get; private set; } = string.Empty;
    public StockForgeApiFactory Factory { get; private set; } = null!;
    public CapturingEmailSender Emails => Factory.Services.GetRequiredService<CapturingEmailSender>();

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("STOCKFORGE_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("postgres:18-alpine").WithUsername("postgres").WithPassword("postgres").Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
        }
        else
        {
            _adminConnectionString = external;
        }

        _databaseName = $"sf_test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{_databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        OwnerConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName }.ConnectionString;
        AppConnectionString = new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        {
            Username = "stockforge_app",
            Password = AppRolePassword,
        }.ConnectionString;

        Factory = new StockForgeApiFactory(this);
        _ = Factory.Server; // Starts the host: applies migrations and seeds roles.
    }

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
        else if (_adminConnectionString is not null && _databaseName is not null)
        {
            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public ApiClient CreateClient(string? accessToken = null) => new(Factory.CreateClient(), accessToken);

    /// <summary>Connection as the runtime role (RLS applies).</summary>
    public NpgsqlConnection OpenAppConnection()
    {
        var connection = new NpgsqlConnection(AppConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Connection as the table owner (RLS bypassed, used to probe database-level constraints).</summary>
    public NpgsqlConnection OpenOwnerConnection()
    {
        var connection = new NpgsqlConnection(OwnerConnectionString);
        connection.Open();
        return connection;
    }
}

public sealed class StockForgeApiFactory(ApiFixture fixture) : WebApplicationFactory<Program>
{
    public static readonly Dictionary<string, string?> BaseSettings = new()
    {
        ["Jwt:SigningKey"] = "integration-tests-signing-key-0123456789abcdef",
        ["Jwt:RefreshReuseGraceSeconds"] = "0",
        ["Database:MigrateOnStartup"] = "true",
        ["Database:SeedOnStartup"] = "true",
        ["Database:AppRolePassword"] = ApiFixture.AppRolePassword,
        ["Seed:DemoData"] = "false",
        ["Seed:SuperAdminEmail"] = "superadmin@tests.local",
        ["Seed:SuperAdminPassword"] = "SuperAdmin!2026",
        ["RateLimiting:AuthPermitsPerMinute"] = "100000",
        ["RateLimiting:RefreshPermitsPerMinute"] = "100000",
        ["RateLimiting:GlobalPermitsPerMinute"] = "1000000",
        ["Https:Redirect"] = "false",
        ["ConnectionStrings:Redis"] = "",
        ["Serilog:MinimumLevel:Default"] = "Warning",
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in BaseSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.UseSetting("ConnectionStrings:Default", fixture.AppConnectionString);
        builder.UseSetting("ConnectionStrings:Migrations", fixture.OwnerConnectionString);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<CapturingEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
            services.AddWorkerJobs();
            services.AddJobHandler<AlwaysFailingJob, AlwaysFailingJobHandler>();
        });
    }
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (_sent)
            {
                return _sent.ToList();
            }
        }
    }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }

        return Task.CompletedTask;
    }

    public EmailMessage? LastTo(string email) => Sent.LastOrDefault(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Job used to exercise retry and dead-lettering.</summary>
public sealed record AlwaysFailingJob(string Reason) : IJob
{
    public static string JobType => "tests.always_failing";
}

public sealed class AlwaysFailingJobHandler : IJobHandler<AlwaysFailingJob>
{
    public Task HandleAsync(AlwaysFailingJob job, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(job.Reason);
}
