namespace StockForge.Domain.Common;

/// <summary>A business rule was violated. <see cref="Code"/> is a stable, machine-readable identifier.</summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public DomainException() : this("domain_error", "A business rule was violated.") { }

    public DomainException(string message) : this("domain_error", message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) => Code = "domain_error";

    public string Code { get; }
}

/// <summary>Input that passed API validation but violates a domain invariant.</summary>
public sealed class DomainValidationException(string message) : DomainException("domain_validation", message);

/// <summary>An operation was attempted from a state that does not allow it.</summary>
public sealed class InvalidStateTransitionException(string entity, string from, string to)
    : DomainException("invalid_state", $"{entity} cannot move from '{from}' to '{to}'.")
{
    public string Entity { get; } = entity;
    public string From { get; } = from;
    public string To { get; } = to;
}

/// <summary>Not enough available stock to satisfy the request.</summary>
public sealed class InsufficientStockException(Guid productId, Guid warehouseId, decimal requested, decimal available)
    : DomainException("insufficient_stock",
        $"Insufficient stock: requested {requested:0.###}, available {available:0.###}.")
{
    public Guid ProductId { get; } = productId;
    public Guid WarehouseId { get; } = warehouseId;
    public decimal Requested { get; } = requested;
    public decimal Available { get; } = available;
}

internal static class Guard
{
    public static string Required(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainValidationException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainValidationException($"{name} must be at most {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength
            ? throw new DomainValidationException($"{name} must be at most {maxLength} characters.")
            : trimmed;
    }

    public static decimal Positive(decimal value, string name) =>
        value > 0 ? value : throw new DomainValidationException($"{name} must be greater than zero.");

    public static decimal NotNegative(decimal value, string name) =>
        value >= 0 ? value : throw new DomainValidationException($"{name} cannot be negative.");

    public static Guid NotEmpty(Guid value, string name) =>
        value != Guid.Empty ? value : throw new DomainValidationException($"{name} is required.");
}
