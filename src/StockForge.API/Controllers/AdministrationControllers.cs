using Microsoft.AspNetCore.Mvc;
using StockForge.API.Authorization;
using StockForge.Application.Audit;
using StockForge.Application.Common;
using StockForge.Application.Identity;
using StockForge.Application.Jobs;
using StockForge.Application.Organization;
using StockForge.Application.Platform;
using StockForge.Application.Security;

namespace StockForge.API.Controllers;

[ApiController]
[Route("api/organization")]
public sealed class OrganizationController(OrganizationService organization) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Organization.View)]
    public Task<OrganizationDto> Get(CancellationToken ct) => organization.GetAsync(ct);

    [HttpPut]
    [HasPermission(Permissions.Organization.Manage)]
    public Task<OrganizationDto> Update(UpdateOrganizationRequest request, CancellationToken ct) => organization.UpdateAsync(request, ct);

    [HttpPut("settings")]
    [HasPermission(Permissions.Organization.Manage)]
    public Task<OrganizationDto> UpdateSettings(TenantSettingsDto settings, CancellationToken ct) => organization.UpdateSettingsAsync(settings, ct);
}

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserManagementService members) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Users.View)]
    public Task<PagedResult<MemberDto>> List([FromQuery] MemberQuery query, CancellationToken ct) => members.ListAsync(query, ct);

    [HttpGet("{userId:guid}")]
    [HasPermission(Permissions.Users.View)]
    public Task<MemberDto> Get(Guid userId, CancellationToken ct) => members.GetAsync(userId, ct);

    [HttpPost]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<ActionResult<MemberDto>> Invite(InviteMemberRequest request, CancellationToken ct)
    {
        var member = await members.InviteAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { userId = member.UserId }, member);
    }

    [HttpPut("{userId:guid}")]
    [HasPermission(Permissions.Users.Manage)]
    public Task<MemberDto> Update(Guid userId, UpdateMemberRequest request, CancellationToken ct) => members.UpdateAsync(userId, request, ct);

    [HttpPost("{userId:guid}/deactivate")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken ct)
    {
        await members.DeactivateAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/activate")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Activate(Guid userId, CancellationToken ct)
    {
        await members.ActivateAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/unlock")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Unlock(Guid userId, CancellationToken ct)
    {
        await members.UnlockAsync(userId, ct);
        return NoContent();
    }

    [HttpPost("{userId:guid}/revoke-sessions")]
    [HasPermission(Permissions.Users.Manage)]
    public async Task<IActionResult> RevokeSessions(Guid userId, CancellationToken ct)
    {
        await members.RevokeSessionsAsync(userId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api")]
public sealed class SecurityCatalogController(IUserManagementService members) : ControllerBase
{
    [HttpGet("roles")]
    [HasPermission(Permissions.Users.View)]
    public Task<IReadOnlyList<RoleDto>> Roles(CancellationToken ct) => members.GetRolesAsync(ct);

    [HttpGet("permissions")]
    [HasPermission(Permissions.Users.View)]
    public IReadOnlyList<PermissionDefinition> AllPermissions() =>
        Permissions.All.Where(p => !p.Name.StartsWith("platform.", StringComparison.Ordinal)).ToList();
}

[ApiController]
[Route("api/platform")]
[HasPermission(Permissions.Platform.ManageTenants)]
public sealed class PlatformController(PlatformService platform, IOnboardingService onboarding) : ControllerBase
{
    [HttpGet("stats")]
    public Task<PlatformStatsDto> Stats(CancellationToken ct) => platform.GetStatsAsync(ct);

    [HttpGet("tenants")]
    public Task<PagedResult<TenantSummaryDto>> Tenants([FromQuery] TenantListQuery query, CancellationToken ct) =>
        platform.ListTenantsAsync(query, ct);

    [HttpPost("tenants")]
    public async Task<ActionResult<CreatedOrganizationDto>> Create(CreateOrganizationRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await onboarding.CreateOrganizationAsync(request, ct));

    [HttpPost("tenants/{tenantId:guid}/suspend")]
    public async Task<IActionResult> Suspend(Guid tenantId, SuspendTenantRequest request, CancellationToken ct)
    {
        await platform.SuspendAsync(tenantId, request, ct);
        return NoContent();
    }

    [HttpPost("tenants/{tenantId:guid}/activate")]
    public async Task<IActionResult> Activate(Guid tenantId, CancellationToken ct)
    {
        await platform.ActivateAsync(tenantId, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/audit-logs")]
public sealed class AuditLogsController(AuditService audit) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Audit.View)]
    public Task<PagedResult<AuditLogDto>> List([FromQuery] AuditQuery query, CancellationToken ct) => audit.ListAsync(query, ct);
}

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(JobMonitoringService jobs) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Jobs.View, Permissions.Platform.ViewJobs)]
    public Task<PagedResult<JobDto>> List([FromQuery] JobQuery query, CancellationToken ct) => jobs.ListAsync(query, ct);

    [HttpGet("stats")]
    [HasPermission(Permissions.Jobs.View, Permissions.Platform.ViewJobs)]
    public Task<JobStatsDto> Stats(CancellationToken ct) => jobs.GetStatsAsync(ct);

    [HttpPost("{id:guid}/retry")]
    [HasPermission(Permissions.Jobs.Manage, Permissions.Platform.ViewJobs)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
    {
        await jobs.RetryAsync(id, ct);
        return NoContent();
    }
}
