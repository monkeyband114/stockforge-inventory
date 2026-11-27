using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;

namespace StockForge.API.Authorization;

/// <summary>
/// Requires the caller to hold a permission. Several permissions separated by <c>|</c> mean "any of";
/// stacking several attributes means "all of".
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute(params string[] anyOf) : AuthorizeAttribute(PolicyPrefix + string.Join('|', anyOf))
{
    public const string PolicyPrefix = "perm:";
}

public sealed class PermissionRequirement(IReadOnlyList<string> anyOf) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}

/// <summary>Builds permission policies on demand, so no policy needs to be registered by hand.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var permissions = policyName[HasPermissionAttribute.PolicyPrefix.Length..].Split('|', StringSplitOptions.RemoveEmptyEntries);
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissions))
            .Build();
    }
}

/// <summary>
/// Checks permissions against the request's membership snapshot (resolved from the database per session),
/// never against claims inside the token.
/// </summary>
public sealed class PermissionAuthorizationHandler(IHttpContextAccessor accessor) : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (accessor.HttpContext?.RequestServices.GetService<ICurrentUser>() is { IsAuthenticated: true } user &&
            requirement.AnyOf.Any(user.HasPermission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
