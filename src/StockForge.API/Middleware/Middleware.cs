using Serilog.Context;
using StockForge.Application.Abstractions;
using StockForge.Infrastructure.Identity;

namespace StockForge.API.Middleware;

/// <summary>Accepts a well-formed <c>X-Correlation-Id</c> or generates one; echoes it and adds it to every log event.</summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string Header = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[Header].ToString();
        var correlationId = incoming.Length is > 0 and <= 64 && incoming.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
            ? incoming
            : context.TraceIdentifier;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[Header] = correlationId;
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>
/// Populates the scoped <see cref="ScopeContext"/> from the authenticated principal and the membership
/// snapshot validated during JWT authentication. This is the only place a request's tenant is set.
/// </summary>
public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public const string SnapshotItem = "stockforge.snapshot";

    public async Task InvokeAsync(HttpContext context, ScopeContext scope)
    {
        scope.SetRequestInfo(
            context.Connection.RemoteIpAddress?.ToString(),
            context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
            context.TraceIdentifier);

        if (context.User.Identity?.IsAuthenticated == true && context.Items[SnapshotItem] is MembershipSnapshot snapshot)
        {
            scope.SetUser(
                snapshot.UserId,
                snapshot.Email,
                snapshot.DisplayName,
                snapshot.SessionId,
                snapshot.IsPlatformAdmin,
                snapshot.TenantId,
                snapshot.Permissions.ToHashSet(StringComparer.Ordinal),
                snapshot.WarehouseIds?.ToHashSet());

            using (LogContext.PushProperty("UserId", snapshot.UserId))
            using (LogContext.PushProperty("TenantId", snapshot.TenantId))
            {
                await next(context);
            }

            return;
        }

        await next(context);
    }
}

/// <summary>Baseline security headers for an API that never serves HTML (except Swagger in development).</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Resource-Policy"] = "same-site";
        if (!context.Request.Path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        }

        return next(context);
    }
}
