using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StockForge.Application.Abstractions;
using StockForge.Domain.Common;
using StockForge.Infrastructure.Persistence.Interceptors;

namespace StockForge.API.Errors;

/// <summary>
/// Maps exceptions to RFC 9457 problem details with a stable machine-readable <c>code</c>. Unexpected errors
/// return a generic 500 with the correlation id only: no stack traces, SQL or internal messages leak out.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var problem = Map(exception);
        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else if (exception is TenantIsolationException or ForbiddenException)
        {
            logger.LogWarning("{ExceptionType}: {Message}", exception.GetType().Name, exception.Message);
        }

        problem.Extensions["traceId"] = httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    internal static ProblemDetails Map(Exception exception) => exception switch
    {
        ValidationException validation => Validation(validation),
        NotFoundException e => Problem(404, "not_found", "Not found", e.Message),
        ForbiddenException e => Problem(403, "forbidden", "Forbidden", e.Message),
        AuthenticationFailedException e => Problem(401, e.Code, "Authentication failed", e.Message),
        ConflictException e => Problem(409, e.Code, "Conflict", e.Message),
        InsufficientStockException e => WithExtensions(Problem(409, e.Code, "Insufficient stock", e.Message), new()
        {
            ["productId"] = e.ProductId,
            ["warehouseId"] = e.WarehouseId,
            ["requested"] = e.Requested,
            ["available"] = e.Available,
        }),
        InvalidStateTransitionException e => Problem(409, e.Code, "Invalid state", e.Message),
        DomainException e => Problem(422, e.Code, "Business rule violated", e.Message),
        TenantIsolationException => Problem(403, "tenant_violation", "Forbidden", "The operation crosses organization boundaries."),
        DbUpdateConcurrencyException => Problem(409, "concurrency_conflict", "Conflict", "The record was modified by someone else. Reload and try again."),
        DbUpdateException { InnerException: PostgresException pg } => FromPostgres(pg),
        PostgresException pg => FromPostgres(pg),
        BadHttpRequestException e => Problem(e.StatusCode, "bad_request", "Bad request", "The request could not be read."),
        _ => Problem(500, "internal_error", "Internal server error", "An unexpected error occurred."),
    };

    private static ProblemDetails FromPostgres(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => Problem(409, "duplicate", "Conflict", "A record with the same unique value already exists."),
        PostgresErrorCodes.ForeignKeyViolation => Problem(422, "invalid_reference", "Invalid reference", "A referenced record does not exist in this organization."),
        PostgresErrorCodes.CheckViolation => Problem(409, "constraint_violation", "Conflict", "The change would violate a data integrity rule (for example negative stock)."),
        PostgresErrorCodes.InsufficientPrivilege => Problem(403, "tenant_violation", "Forbidden", "The operation crosses organization boundaries."),
        PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected =>
            Problem(409, "concurrency_conflict", "Conflict", "The data changed while your request was processed. Please retry."),
        _ => Problem(500, "internal_error", "Internal server error", "An unexpected error occurred."),
    };

    private static ProblemDetails Validation(ValidationException exception)
    {
        var errors = exception.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        var problem = new ValidationProblemDetails(errors)
        {
            Status = 400,
            Title = "Validation failed",
            Detail = errors.Count == 1 && errors.First().Value.Length == 1 ? errors.First().Value[0] : "One or more fields are invalid.",
        };
        problem.Extensions["code"] = "validation_failed";
        return problem;
    }

    private static ProblemDetails Problem(int status, string code, string title, string detail)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return problem;
    }

    private static ProblemDetails WithExtensions(ProblemDetails problem, Dictionary<string, object?> extensions)
    {
        foreach (var (key, value) in extensions)
        {
            problem.Extensions[key] = value;
        }

        return problem;
    }

    private static string ToCamelCase(string name) =>
        string.Join('.', name.Split('.').Select(part => part.Length == 0 ? part : char.ToLowerInvariant(part[0]) + part[1..]));
}
