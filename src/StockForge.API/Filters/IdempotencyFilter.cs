using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Infrastructure.Persistence;

namespace StockForge.API.Filters;

/// <summary>
/// Makes a POST endpoint safe to retry. With an <c>Idempotency-Key</c> header, the first request executes and
/// its response is stored; retries with the same key and payload receive the stored response
/// (<c>Idempotent-Replayed: true</c>) without executing again. Reusing a key for a different payload is
/// rejected, and a retry that arrives while the original is still running gets 409.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IFilterFactory
{
    /// <summary>When true, requests without a key are rejected.</summary>
    public bool Required { get; init; }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider) => new IdempotencyFilter(Required);
}

public sealed class IdempotencyFilter(bool required) : IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[Header].ToString();
        if (string.IsNullOrEmpty(key))
        {
            if (required)
            {
                context.Result = Problem(400, "idempotency_key_required", $"The {Header} header is required for this operation.");
                return;
            }

            await next();
            return;
        }

        if (key.Length > 100 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.'))
        {
            context.Result = Problem(400, "invalid_idempotency_key", $"{Header} must be 1–100 characters of letters, digits, '-', '_', ':' or '.'.");
            return;
        }

        var services = http.RequestServices;
        var user = services.GetRequiredService<ICurrentUser>();
        var store = services.GetRequiredService<IdempotencyStore>();
        var json = services.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
        var requestHash = Hash(http.Request.Method, http.Request.Path, context.ActionArguments, json);

        var claim = await store.TryClaimAsync(key, user.RequiredUserId, http.Request.Method, http.Request.Path, requestHash, http.RequestAborted);
        switch (claim.Claim)
        {
            case IdempotencyClaim.Completed:
                http.Response.Headers[ReplayedHeader] = "true";
                context.Result = new ContentResult
                {
                    StatusCode = claim.Record!.ResponseStatusCode,
                    Content = claim.Record.ResponseBody,
                    ContentType = claim.Record.ResponseContentType,
                };
                return;
            case IdempotencyClaim.InProgress:
                http.Response.Headers.RetryAfter = "1";
                context.Result = Problem(409, "request_in_progress", "A request with this idempotency key is still being processed.");
                return;
            case IdempotencyClaim.Mismatch:
                context.Result = Problem(422, "idempotency_key_reused", "This idempotency key was already used for a different request.");
                return;
        }

        var executed = await next();
        if (executed.Exception is null && executed.Result is ObjectResult { StatusCode: null or < 500 } result)
        {
            var body = JsonSerializer.Serialize(result.Value, result.DeclaredType ?? result.Value?.GetType() ?? typeof(object), json);
            await store.CompleteAsync(key, result.StatusCode ?? 200, body, "application/json; charset=utf-8", CancellationToken.None);
        }
        else if (executed.Exception is null && executed.Result is StatusCodeResult { StatusCode: < 500 } status)
        {
            await store.CompleteAsync(key, status.StatusCode, null, null, CancellationToken.None);
        }
        else
        {
            // Failures are not cached: the client may retry with the same key once the cause is fixed.
            await store.ReleaseAsync(key, CancellationToken.None);
        }
    }

    private static string Hash(string method, string path, IDictionary<string, object?> arguments, JsonSerializerOptions json)
    {
        var payload = JsonSerializer.Serialize(arguments.Where(a => a.Value is not CancellationToken).OrderBy(a => a.Key).ToDictionary(a => a.Key, a => a.Value), json);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{method}\n{path}\n{payload}")));
    }

    private static ObjectResult Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = "Idempotency", Detail = detail };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
