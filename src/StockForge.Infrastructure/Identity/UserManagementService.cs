using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Application.Common;
using StockForge.Application.Identity;
using StockForge.Application.Jobs;
using StockForge.Application.Security;
using StockForge.Domain.Common;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Email;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Identity;

/// <summary>Creates (or reuses) a user account for a new membership and sends the right email.</summary>
internal sealed class MemberInvitations(
    UserManager<ApplicationUser> userManager,
    IJobQueue jobs,
    IOptions<AppUrlOptions> urls,
    TimeProvider clock)
{
    public async Task<(ApplicationUser User, bool Created)> EnsureUserAsync(string email, string firstName, string lastName)
    {
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            return (existing, false);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            CreatedAt = clock.GetUtcNow(),
        };
        (await userManager.CreateAsync(user)).EnsureSucceeded();
        return (user, true);
    }

    public async Task NotifyAsync(ApplicationUser user, bool created, string organizationName)
    {
        var baseUrl = urls.Value.FrontendBaseUrl.TrimEnd('/');
        if (created)
        {
            var token = await userManager.GenerateUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose);
            var link = $"{baseUrl}/accept-invitation?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
            var (subject, html, text) = EmailTemplates.Invitation(user.FirstName, organizationName, link);
            jobs.Enqueue(new SendEmailJob(user.Email!, subject, html, text), new JobOptions { Sensitive = true, MaxAttempts = 5 });
        }
        else
        {
            var (subject, html, text) = EmailTemplates.AddedToOrganization(user.FirstName, organizationName, $"{baseUrl}/login");
            jobs.Enqueue(new SendEmailJob(user.Email!, subject, html, text));
        }
    }
}

internal sealed class UserManagementService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    ScopeContext scope,
    ITransactionRunner transactions,
    IMembershipSnapshotProvider snapshots,
    MemberInvitations invitations,
    AuditTrail audit,
    TimeProvider clock) : IUserManagementService
{
    public async Task<PagedResult<MemberDto>> ListAsync(MemberQuery query, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var members =
            from m in db.TenantMemberships
            join u in db.Users on m.UserId equals u.Id
            select new { m, u };

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            members = members.Where(x => x.m.Roles.Contains(query.Role));
        }

        if (query.IsActive is { } isActive)
        {
            members = members.Where(x => x.m.IsActive == isActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            members = members.Where(x =>
                x.u.FirstName.ToLower().Contains(term) || x.u.LastName.ToLower().Contains(term) ||
                (x.u.Email != null && x.u.Email.ToLower().Contains(term)));
        }

        return await members
            .OrderBy(x => x.u.FirstName).ThenBy(x => x.u.LastName)
            .Select(x => new MemberDto(
                x.m.Id, x.u.Id, x.u.Email!, x.u.FirstName, x.u.LastName, x.m.JobTitle, x.m.IsActive && x.u.IsActive,
                x.u.LockoutEnd != null && x.u.LockoutEnd > now, x.m.Roles, x.m.GrantedPermissions,
                x.m.WarehouseAssignments.Select(a => a.WarehouseId).ToList(), x.m.JoinedAt, x.u.LastLoginAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<MemberDto> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return await (
                from m in db.TenantMemberships
                join u in db.Users on m.UserId equals u.Id
                where m.UserId == userId
                select new MemberDto(
                    m.Id, u.Id, u.Email!, u.FirstName, u.LastName, m.JobTitle, m.IsActive && u.IsActive,
                    u.LockoutEnd != null && u.LockoutEnd > now, m.Roles, m.GrantedPermissions,
                    m.WarehouseAssignments.Select(a => a.WarehouseId).ToList(), m.JoinedAt, u.LastLoginAt))
            .FirstOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Member", userId);
    }

    public async Task<MemberDto> InviteAsync(InviteMemberRequest request, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId ?? throw new ForbiddenException("No organization is selected.");
        await EnsureCanAssignAsync(request.Roles, [], cancellationToken);
        await EnsureWarehousesExistAsync(request.WarehouseIds ?? [], cancellationToken);
        var email = request.Email.Trim();

        var userId = await transactions.ExecuteAsync(async token =>
        {
            var (user, created) = await invitations.EnsureUserAsync(email, request.FirstName, request.LastName);
            if (await db.TenantMemberships.AnyAsync(m => m.UserId == user.Id, token))
            {
                throw new ConflictException("already_member", "This person is already a member of the organization.");
            }

            var membership = new TenantMembership(tenantId, user.Id, request.Roles, request.JobTitle, clock.GetUtcNow());
            membership.AssignWarehouses(request.WarehouseIds ?? []);
            db.TenantMemberships.Add(membership);

            var organization = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.Name).FirstAsync(token);
            await invitations.NotifyAsync(user, created, organization);
            audit.Record("MemberInvited", "User", user.Id.ToString(), new { user.Email, request.Roles, NewAccount = created });
            await db.SaveChangesAsync(token);
            return user.Id;
        }, cancellationToken);

        return await GetAsync(userId, cancellationToken);
    }

    public async Task<MemberDto> UpdateAsync(Guid userId, UpdateMemberRequest request, CancellationToken cancellationToken)
    {
        var membership = await LoadMembershipAsync(userId, cancellationToken);
        await EnsureCanAssignAsync(request.Roles, request.GrantedPermissions, cancellationToken);
        await EnsureWarehousesExistAsync(request.WarehouseIds, cancellationToken);
        if (membership.Roles.Contains(RoleNames.OrganizationAdmin) && !request.Roles.Contains(RoleNames.OrganizationAdmin))
        {
            await EnsureAnotherAdminAsync(membership.Id, cancellationToken);
        }

        membership.SetRoles(request.Roles);
        membership.SetGrantedPermissions(request.GrantedPermissions);
        membership.AssignWarehouses(request.WarehouseIds);
        membership.UpdateJobTitle(request.JobTitle);
        await db.SaveChangesAsync(cancellationToken);
        await snapshots.InvalidateUserAsync(userId, cancellationToken);
        return await GetAsync(userId, cancellationToken);
    }

    public async Task DeactivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == scope.UserId)
        {
            throw new DomainException("cannot_deactivate_self", "You cannot deactivate your own membership.");
        }

        var membership = await LoadMembershipAsync(userId, cancellationToken);
        if (membership.Roles.Contains(RoleNames.OrganizationAdmin))
        {
            await EnsureAnotherAdminAsync(membership.Id, cancellationToken);
        }

        membership.Deactivate();
        await RevokeTenantSessionsAsync(userId, "membership_deactivated", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await snapshots.InvalidateUserAsync(userId, cancellationToken);
    }

    public async Task ActivateAsync(Guid userId, CancellationToken cancellationToken)
    {
        var membership = await LoadMembershipAsync(userId, cancellationToken);
        membership.Activate();
        await db.SaveChangesAsync(cancellationToken);
        await snapshots.InvalidateUserAsync(userId, cancellationToken);
    }

    public async Task UnlockAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = await LoadMembershipAsync(userId, cancellationToken);
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Member", userId);
        (await userManager.SetLockoutEndDateAsync(user, null)).EnsureSucceeded();
        (await userManager.ResetAccessFailedCountAsync(user)).EnsureSucceeded();
        audit.Record("AccountUnlocked", "User", userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await snapshots.InvalidateUserAsync(userId, cancellationToken);
    }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        _ = await LoadMembershipAsync(userId, cancellationToken);
        await RevokeTenantSessionsAsync(userId, "revoked_by_admin", cancellationToken);
        audit.Record("SessionsRevoked", "User", userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().Where(r => !r.IsPlatformRole).ToListAsync(cancellationToken);
        var claims = await db.RoleClaims.AsNoTracking()
            .Where(c => c.ClaimType == Permissions.ClaimType)
            .Select(c => new { c.RoleId, c.ClaimValue })
            .ToListAsync(cancellationToken);
        return roles
            .OrderBy(r => RoleNames.TenantRoles.ToList().IndexOf(r.Name!))
            .Select(r => new RoleDto(r.Name!, r.Description,
                claims.Where(c => c.RoleId == r.Id).Select(c => c.ClaimValue!).Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }

    private async Task<TenantMembership> LoadMembershipAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.TenantMemberships.Include(m => m.WarehouseAssignments).FirstOrDefaultAsync(m => m.UserId == userId, cancellationToken)
        ?? throw new NotFoundException("Member", userId);

    /// <summary>Prevents privilege escalation: a member can only hand out permissions they hold themselves.</summary>
    private async Task EnsureCanAssignAsync(IReadOnlyCollection<string> roles, IReadOnlyCollection<string> grants, CancellationToken cancellationToken)
    {
        var rolePermissions = await (
                from rc in db.RoleClaims
                join r in db.Roles on rc.RoleId equals r.Id
                where roles.Contains(r.Name!) && rc.ClaimType == Permissions.ClaimType
                select rc.ClaimValue!)
            .ToListAsync(cancellationToken);
        var missing = rolePermissions.Concat(grants).Distinct().Where(p => !scope.Permissions.Contains(p)).ToList();
        if (missing.Count > 0)
        {
            throw new ForbiddenException("You cannot grant permissions you do not hold yourself.");
        }
    }

    private async Task EnsureWarehousesExistAsync(IReadOnlyCollection<Guid> warehouseIds, CancellationToken cancellationToken)
    {
        var ids = warehouseIds.Distinct().ToList();
        if (ids.Count > 0 && await db.Warehouses.CountAsync(w => ids.Contains(w.Id), cancellationToken) != ids.Count)
        {
            throw new NotFoundException("Warehouse");
        }
    }

    private async Task EnsureAnotherAdminAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        var otherAdmins = await db.TenantMemberships.CountAsync(
            m => m.Id != membershipId && m.IsActive && m.Roles.Contains(RoleNames.OrganizationAdmin), cancellationToken);
        if (otherAdmins == 0)
        {
            throw new DomainException("last_admin", "An organization must keep at least one active administrator.");
        }
    }

    private async Task RevokeTenantSessionsAsync(Guid userId, string reason, CancellationToken cancellationToken)
    {
        var tenantId = scope.TenantId;
        var now = clock.GetUtcNow();
        var sessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.TenantId == tenantId && s.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now, reason);
            await db.RefreshTokens.Where(t => t.SessionId == session.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);
            await snapshots.InvalidateSessionAsync(session.Id, cancellationToken);
        }
    }
}

/// <summary>
/// Platform-administrator onboarding. Runs in a child DI scope so the new organization's tenant context
/// never mixes with whatever organization the administrator currently has selected.
/// </summary>
internal sealed class OnboardingService(IServiceScopeFactory scopeFactory, ScopeContext callerScope) : IOnboardingService
{
    public async Task<CreatedOrganizationDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        await using var child = scopeFactory.CreateAsyncScope();
        var services = child.ServiceProvider;
        var scope = services.GetRequiredService<ScopeContext>();
        scope.SetRequestInfo(callerScope.IpAddress, callerScope.UserAgent, callerScope.CorrelationId);
        scope.SetUser(callerScope.UserId ?? Guid.Empty, callerScope.Email, callerScope.DisplayName, callerScope.SessionId,
            callerScope.IsPlatformAdmin, null, callerScope.Permissions, null);

        var provisioner = services.GetRequiredService<TenantProvisioner>();
        var tenant = await provisioner.PrepareAsync(request.OrganizationName, request.AdminEmail.Trim(), request.BaseCurrency, request.TimeZone, cancellationToken);
        scope.EnterTenant(tenant.Id);

        var db = services.GetRequiredService<AppDbContext>();
        var invitations = services.GetRequiredService<MemberInvitations>();
        var audit = services.GetRequiredService<AuditTrail>();
        var clock = services.GetRequiredService<TimeProvider>();

        var (userId, created) = await services.GetRequiredService<ITransactionRunner>().ExecuteAsync(async token =>
        {
            provisioner.Provision(tenant);
            var (user, isNew) = await invitations.EnsureUserAsync(request.AdminEmail.Trim(), request.AdminFirstName, request.AdminLastName);
            db.TenantMemberships.Add(new TenantMembership(tenant.Id, user.Id, [RoleNames.OrganizationAdmin], "Administrator", clock.GetUtcNow()));
            await invitations.NotifyAsync(user, isNew, tenant.Name);
            audit.Record("OrganizationCreated", nameof(Tenant), tenant.Id.ToString(), new { tenant.Name, AdminEmail = user.Email });
            await db.SaveChangesAsync(token);
            return (user.Id, isNew);
        }, cancellationToken);

        return new CreatedOrganizationDto(tenant.Id, tenant.Name, tenant.Slug, userId, created);
    }
}
