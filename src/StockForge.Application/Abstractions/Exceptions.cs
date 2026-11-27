namespace StockForge.Application.Abstractions;

/// <summary>The resource does not exist or is not visible to the current tenant (never reveals which).</summary>
public sealed class NotFoundException(string resource, object? key = null)
    : Exception(key is null ? $"{resource} was not found." : $"{resource} '{key}' was not found.")
{
    public string Resource { get; } = resource;
}

/// <summary>The caller is authenticated but not allowed to perform the operation.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>The request conflicts with current state (duplicates, concurrent modification).</summary>
public sealed class ConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Authentication failed (bad credentials, revoked session, locked account).</summary>
public sealed class AuthenticationFailedException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
