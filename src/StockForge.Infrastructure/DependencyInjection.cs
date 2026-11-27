using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;
using StockForge.Application;
using StockForge.Application.Abstractions;
using StockForge.Application.Identity;
using StockForge.Infrastructure.Email;
using StockForge.Infrastructure.Identity;
using StockForge.Infrastructure.Jobs;
using StockForge.Infrastructure.Persistence;
using StockForge.Infrastructure.Persistence.Interceptors;
using StockForge.Infrastructure.Realtime;
using StockForge.Infrastructure.Seeding;

namespace StockForge.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence, identity, caching, jobs, email and realtime services shared by API and worker.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<AdjustableClock>();
        services.TryAddSingleton<TimeProvider>(sp => sp.GetRequiredService<AdjustableClock>());
        services.AddApplication();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.Section));
        services.Configure<AppUrlOptions>(configuration.GetSection(AppUrlOptions.Section));
        services.Configure<OutboxOptions>(configuration.GetSection("Outbox"));
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.Section));
        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.Section));

        // ---- Persistence ----
        var connectionString = configuration.GetConnectionString("Default")
                               ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<TenantTransactionInterceptor>();
        services.AddScoped<AuditingInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            AppDbContextOptions.Configure(options, connectionString)
                .AddInterceptors(
                    sp.GetRequiredService<TenantConnectionInterceptor>(),
                    sp.GetRequiredService<TenantTransactionInterceptor>(),
                    sp.GetRequiredService<AuditingInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<DomainEventBuffer>();
        services.AddScoped<IDomainEventDispatcher, RealtimeDomainEventDispatcher>();
        services.AddScoped<ITransactionRunner, TransactionRunner>();
        services.AddScoped<ISequenceGenerator, SequenceGenerator>();
        services.AddScoped<AuditTrail>();
        services.AddScoped<IAuditTrail>(sp => sp.GetRequiredService<AuditTrail>());
        services.AddScoped<IFileStorage, DatabaseFileStorage>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<IdempotencyStore>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddScoped<IdentitySeeder>();
        services.AddScoped<DevelopmentDataSeeder>();

        // ---- Identity ----
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<InvitationTokenProvider>(InvitationTokenProvider.ProviderName);
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.Configure<InvitationTokenProviderOptions>(_ => { });

        // Keys live in PostgreSQL so every instance can read tokens/payloads protected by any other.
        services.AddDataProtection().SetApplicationName("StockForge").PersistKeysToDbContext<AppDbContext>();

        services.AddSingleton<TokenService>();
        services.AddScoped<MembershipSnapshotProvider>();
        services.AddScoped<IMembershipSnapshotProvider>(sp => sp.GetRequiredService<MembershipSnapshotProvider>());
        services.AddScoped<ISessionCache>(sp => sp.GetRequiredService<MembershipSnapshotProvider>());
        services.AddScoped<TenantProvisioner>();
        services.AddScoped<MemberInvitations>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IOnboardingService, OnboardingService>();

        // ---- Cache ----
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            // Single-instance development only: horizontal scaling requires Redis.
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(o =>
            {
                o.Configuration = redis;
                o.InstanceName = "stockforge:";
            });
        }

        // ---- Jobs / email / realtime ----
        services.AddSingleton<JobPayloadProtector>();
        services.AddScoped<IJobQueue, OutboxJobQueue>();
        services.AddSingleton(sp =>
        {
            var registry = new JobRegistry();
            foreach (var register in sp.GetServices<Action<JobRegistry>>())
            {
                register(registry);
            }

            return registry;
        });
        services.AddSingleton<OutboxProcessor>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IRealtimeNotifier, SignalRRealtimeNotifier>();
        return services;
    }

    /// <summary>SignalR with the Redis backplane when Redis is configured (required for more than one instance).</summary>
    public static IServiceCollection AddStockForgeSignalR(this IServiceCollection services, IConfiguration configuration)
    {
        var signalR = services.AddSignalR(o =>
        {
            o.EnableDetailedErrors = false;
            o.MaximumReceiveMessageSize = 32 * 1024;
        });
        var redis = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redis))
        {
            signalR.AddStackExchangeRedis(redis, o => o.Configuration.ChannelPrefix = RedisChannel.Literal("stockforge"));
        }

        return services;
    }
}
