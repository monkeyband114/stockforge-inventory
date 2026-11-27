using Serilog;
using StockForge.Application.Jobs;
using StockForge.Infrastructure;
using StockForge.Infrastructure.Jobs;
using StockForge.Worker.Handlers;
using StockForge.Worker.Services;

namespace StockForge.Worker;

/// <summary>Background worker host: outbox processing and scheduled jobs. Scale horizontally as needed.</summary>
public static class WorkerProgram
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "StockForge.Worker"));

        builder.Services.AddInfrastructure(builder.Configuration);

        // SignalR is registered only to obtain IHubContext: with the Redis backplane, messages published here
        // reach clients connected to any API instance.
        builder.Services.AddStockForgeSignalR(builder.Configuration);
        builder.Services.AddWorkerJobs();
        builder.Services.Configure<SchedulerOptions>(builder.Configuration.GetSection("Scheduler"));
        builder.Services.AddHostedService<OutboxWorker>();
        builder.Services.AddHostedService<ScheduledJobsWorker>();

        await builder.Build().RunAsync();
    }
}

public static class WorkerRegistration
{
    /// <summary>Registers every job handler. Add new background jobs here.</summary>
    public static IServiceCollection AddWorkerJobs(this IServiceCollection services) => services
        .AddJobHandler<SendEmailJob, SendEmailJobHandler>()
        .AddJobHandler<LowStockAlertJob, LowStockAlertJobHandler>()
        .AddJobHandler<DailyInventorySummaryJob, DailyInventorySummaryJobHandler>();
}
