using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using StockForge.API.Middleware;
using StockForge.Infrastructure.Identity;
using StockForge.Infrastructure.Realtime;

namespace StockForge.API.Hosting;

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Refresh = "refresh";
}

public sealed class RateLimitOptions
{
    public int AuthPermitsPerMinute { get; set; } = 10;
    public int RefreshPermitsPerMinute { get; set; } = 60;
    public int GlobalPermitsPerMinute { get; set; } = 600;
}

internal static class ApiSupport
{
    /// <summary>
    /// JWT bearer events: tokens for the SignalR hub may arrive in the query string (WebSockets cannot send
    /// headers); every validated token is checked against its live session/membership snapshot.
    /// </summary>
    public static JwtBearerEvents CreateJwtEvents() => new()
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"].ToString();
            if (token.Length > 0 && context.HttpContext.Request.Path.StartsWithSegments(NotificationsHub.Path))
            {
                context.Token = token;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var provider = context.HttpContext.RequestServices.GetRequiredService<IMembershipSnapshotProvider>();
            var snapshot = await provider.GetForPrincipalAsync(context.Principal!, context.HttpContext.RequestAborted);
            if (snapshot is null)
            {
                context.Fail("session_invalid");
                return;
            }

            context.HttpContext.Items[RequestContextMiddleware.SnapshotItem] = snapshot;
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            var code = context.AuthenticateFailure switch
            {
                null => "unauthenticated",
                Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException => "token_expired",
                { Message: "session_invalid" } => "session_invalid",
                _ => "invalid_token",
            };
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            var problem = new ProblemDetails { Status = 401, Title = "Authentication required", Detail = "A valid access token is required." };
            problem.Extensions["code"] = code;
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        },
        OnForbidden = async context =>
        {
            var problem = new ProblemDetails { Status = 403, Title = "Forbidden", Detail = "You do not have permission to perform this action." };
            problem.Extensions["code"] = "forbidden";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
        },
    };

    public static void ConfigureRateLimiting(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions options, RateLimitOptions limits)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, token) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            var problem = new ProblemDetails { Status = 429, Title = "Too many requests", Detail = "Rate limit exceeded. Please slow down." };
            problem.Extensions["code"] = "rate_limited";
            await context.HttpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", token);
        };

        // Brute-force protection for anonymous credential endpoints, per client IP.
        options.AddPolicy(RateLimitPolicies.Auth, http => RateLimitPartition.GetFixedWindowLimiter(
            ClientIp(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.AuthPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        options.AddPolicy(RateLimitPolicies.Refresh, http => RateLimitPartition.GetFixedWindowLimiter(
            ClientIp(http), _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.RefreshPermitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

        // Per-user (or per-IP when anonymous) ceiling on everything else.
        options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
        {
            var key = http.User.FindFirstValue(StockForgeClaims.UserId) is { } userId ? $"u:{userId}" : $"ip:{ClientIp(http)}";
            return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = limits.GlobalPermitsPerMinute,
                TokensPerPeriod = limits.GlobalPermitsPerMinute,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }

    private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

/// <summary>Adds the bearer security scheme to the OpenAPI document so Swagger UI can call protected endpoints.</summary>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "StockForge API",
            Version = "v1",
            Description = "Multi-tenant inventory, order management and fulfillment platform. Authenticate with POST /api/auth/login and use the access token as a Bearer token.",
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Access token from /api/auth/login",
        };
        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        return Task.CompletedTask;
    }
}

/// <summary>Readiness check for the distributed cache (Redis in production).</summary>
internal sealed class DistributedCacheHealthCheck(IDistributedCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.SetStringAsync("sf:health", "ok", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }, cancellationToken);
            return await cache.GetStringAsync("sf:health", cancellationToken) == "ok"
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Degraded("Cache round-trip returned an unexpected value.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Cache unavailable.", ex);
        }
    }
}
