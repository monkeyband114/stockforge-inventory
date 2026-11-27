using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Serilog;
using StockForge.API.Authorization;
using StockForge.API.Errors;
using StockForge.API.Filters;
using StockForge.API.Hosting;
using StockForge.API.Middleware;
using StockForge.Infrastructure;
using StockForge.Infrastructure.Identity;
using StockForge.Infrastructure.Persistence;
using StockForge.Infrastructure.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "StockForge.API"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddStockForgeSignalR(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services
    .AddControllers(options => options.Filters.Add<ValidationFilter>())
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    });
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context =>
    {
        var problem = new ValidationProblemDetails(context.ModelState) { Status = 400, Title = "Validation failed" };
        problem.Extensions["code"] = "validation_failed";
        problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    });
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// ---- Authentication & authorization ----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<TokenService>((options, tokens) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = tokens.CreateValidationParameters();
        options.Events = ApiSupport.CreateJwtEvents();
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// ---- Rate limiting, CORS, forwarded headers ----
var limits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddRateLimiter(options => ApiSupport.ConfigureRateLimiting(options, limits));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowCredentials()
    .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "X-Correlation-Id", "X-Requested-With", "x-signalr-user-agent")
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
    .WithExposedHeaders("X-Correlation-Id", "Idempotent-Replayed", "Retry-After")));

if (builder.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    // Only enable behind a trusted reverse proxy / load balancer.
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgres", tags: ["ready"])
    .AddCheck<DistributedCacheHealthCheck>("cache", tags: ["ready"]);

var app = builder.Build();

// ---- Database: `--migrate` runs migrations + reference seed and exits (used by the compose migrator) ----
var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (args.Contains("--migrate"))
{
    await initializer.MigrateAsync(CancellationToken.None);
    await initializer.SeedAsync(CancellationToken.None);
    return;
}

if (database.MigrateOnStartup)
{
    await initializer.MigrateAsync(CancellationToken.None);
}

if (database.SeedOnStartup)
{
    await initializer.SeedAsync(CancellationToken.None);
}

if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<CorrelationIdMiddleware>();
// Outside the exception handler, so the logged status is the one the client actually received.
app.UseSerilogRequestLogging(options =>
{
    // RequestPath excludes the query string, so SignalR access tokens never reach the logs.
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";
    options.GetLevel = (http, _, ex) => ex is not null || http.Response.StatusCode >= 500
        ? Serilog.Events.LogEventLevel.Error
        : http.Request.Path.StartsWithSegments("/health") ? Serilog.Events.LogEventLevel.Verbose : Serilog.Events.LogEventLevel.Information;
});
app.UseExceptionHandler();
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment() && app.Configuration.GetValue("Https:Redirect", true))
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors("web");
app.UseAuthentication();
app.UseMiddleware<RequestContextMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationsHub>(NotificationsHub.Path);
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "StockForge API v1");
        options.DocumentTitle = "StockForge API";
    });
}

await app.RunAsync();

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program;
