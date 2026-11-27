using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockForge.Application.Abstractions;
using StockForge.Application.Identity;
using StockForge.Application.Jobs;
using StockForge.Application.Security;
using StockForge.Domain.Tenancy;
using StockForge.Infrastructure.Email;
using StockForge.Infrastructure.Persistence;

namespace StockForge.Infrastructure.Identity;

/// <summary>
/// Sign-in, organization onboarding, refresh-token rotation with reuse detection, session management and
/// password flows. Failures never reveal whether an account exists.
/// </summary>
internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    ScopeContext scope,
    TokenService tokens,
    IMembershipSnapshotProvider snapshots,
    TenantProvisioner provisioner,
    ITransactionRunner transactions,
    AuditTrail audit,
    IJobQueue jobs,
    IOptions<JwtOptions> jwtOptions,
    IOptions<AppUrlOptions> urls,
    TimeProvider clock,
    ILogger<AuthService> logger) : IAuthService
{
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString()));

    public async Task<AuthResult> RegisterOrganizationAsync(RegisterOrganizationRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new ConflictException("email_taken",
                "An account with this email already exists. Sign in instead, or register with a different email.");
        }

        var tenant = await provisioner.PrepareAsync(request.OrganizationName, email, request.BaseCurrency, request.TimeZone, cancellationToken);
        scope.EnterTenant(tenant.Id);
        var now = clock.GetUtcNow();

        var user = await transactions.ExecuteAsync(async token =>
        {
            provisioner.Provision(tenant);
            var created = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                CreatedAt = now,
            };
            (await userManager.CreateAsync(created, request.Password)).EnsureSucceeded();
            db.TenantMemberships.Add(new TenantMembership(tenant.Id, created.Id, [RoleNames.OrganizationAdmin], "Owner", now));
            audit.RecordFor(tenant.Id, created.Id, email, "OrganizationRegistered", nameof(Tenant), tenant.Id.ToString(), new { tenant.Name, tenant.Slug });
            await db.SaveChangesAsync(token);
            return created;
        }, cancellationToken);

        return await IssueSessionAsync(user, tenant.Id, client, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive)
        {
            // Equalize timing with a real password check so response time does not reveal account existence.
            userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyHash.Value, request.Password);
            throw InvalidCredentials();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            throw AccountLocked();
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            var locked = await userManager.IsLockedOutAsync(user);
            audit.RecordFor(user.LastTenantId, user.Id, user.Email, locked ? "AccountLocked" : "SignInFailed", "User", user.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
            throw locked ? AccountLocked() : InvalidCredentials();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        var isPlatformAdmin = await userManager.IsInRoleAsync(user, RoleNames.SuperAdmin);
        var tenantIds = await ActiveTenantIdsAsync(user.Id, cancellationToken);

        Guid? tenantId;
        if (request.TenantId is { } requested)
        {
            tenantId = tenantIds.Contains(requested)
                ? requested
                : throw new AuthenticationFailedException("not_a_member", "You are not an active member of that organization.");
        }
        else if (user.LastTenantId is { } last && tenantIds.Contains(last))
        {
            tenantId = last;
        }
        else
        {
            tenantId = tenantIds.Count > 0 ? tenantIds[0] : null;
        }

        if (tenantId is null && !isPlatformAdmin)
        {
            throw new AuthenticationFailedException("no_active_membership", "Your account is not active in any organization.");
        }

        return await IssueSessionAsync(user, tenantId, client, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var jwt = jwtOptions.Value;
        var hash = RefreshToken.Hash(refreshToken);
        var stored = await db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
                     ?? throw InvalidRefreshToken();
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == stored.SessionId, cancellationToken)
                      ?? throw InvalidRefreshToken();

        if (stored.RevokedAt is { } revokedAt)
        {
            // A rotated token presented again: either two tabs refreshing at once (benign, within the grace
            // window) or a stolen token being replayed — in which case the whole session is revoked.
            var benignRace = stored.ReplacedByTokenId is not null && session.IsActive(now) &&
                             now - revokedAt <= TimeSpan.FromSeconds(jwt.RefreshReuseGraceSeconds);
            if (!benignRace)
            {
                await RevokeSessionAsync(session, "refresh_token_reuse", cancellationToken);
                audit.RecordFor(session.TenantId, session.UserId, null, "RefreshTokenReuseDetected", nameof(UserSession),
                    session.Id.ToString(), new { client.IpAddress });
                await db.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Refresh token reuse detected; session {SessionId} revoked", session.Id);
                throw InvalidRefreshToken();
            }
        }
        else if (stored.ExpiresAt <= now)
        {
            throw SessionExpired();
        }

        if (!session.IsActive(now))
        {
            throw SessionExpired();
        }

        var user = await userManager.FindByIdAsync(session.UserId.ToString());
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user) ||
            (session.TenantId is { } tenantId && !(await ActiveTenantIdsAsync(user.Id, cancellationToken)).Contains(tenantId)))
        {
            await RevokeSessionAsync(session, "access_revoked", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("access_revoked", "Your access has been revoked. Please contact your administrator.");
        }

        var expires = Min(now.AddDays(jwt.RefreshTokenDays), session.CreatedAt.AddDays(jwt.SessionMaxDays));
        if (expires <= now)
        {
            throw SessionExpired();
        }

        var (token, entity) = RefreshToken.Issue(session.Id, user.Id, now, expires, client.IpAddress);
        if (stored.RevokedAt is null)
        {
            // Atomic claim: if a concurrent request rotated the token first this updates 0 rows, which is the
            // same benign race handled above.
            await db.RefreshTokens
                .Where(t => t.Id == stored.Id && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokedReason, "rotated")
                    .SetProperty(t => t.ReplacedByTokenId, entity.Id), cancellationToken);
        }

        db.RefreshTokens.Add(entity);
        session.Touch(now, expires);
        await db.SaveChangesAsync(cancellationToken);
        return await BuildResultAsync(user, session, token, cancellationToken);
    }

    public async Task<AuthResult> SwitchTenantAsync(Guid tenantId, ClientInfo client, CancellationToken cancellationToken)
    {
        var userId = scope.UserId ?? throw new ForbiddenException("Authentication is required.");
        if (!(await ActiveTenantIdsAsync(userId, cancellationToken)).Contains(tenantId))
        {
            throw new ForbiddenException("You are not an active member of that organization.");
        }

        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException("Authentication is required.");
        if (scope.SessionId is { } currentSessionId &&
            await db.UserSessions.FirstOrDefaultAsync(s => s.Id == currentSessionId, cancellationToken) is { } current)
        {
            await RevokeSessionAsync(current, "switched_organization", cancellationToken);
        }

        return await IssueSessionAsync(user, tenantId, client, cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        if (scope.SessionId is not { } sessionId)
        {
            return;
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is not null)
        {
            await RevokeSessionAsync(session, "logout", cancellationToken);
            audit.RecordFor(session.TenantId, session.UserId, scope.Email, "SignedOut", nameof(UserSession), session.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive)
        {
            logger.LogInformation("Password reset requested for an unknown or inactive account");
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{urls.Value.FrontendBaseUrl.TrimEnd('/')}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
        var (subject, html, text) = EmailTemplates.PasswordReset(user.FirstName, link);
        jobs.Enqueue(new SendEmailJob(user.Email!, subject, html, text), new JobOptions { Sensitive = true, MaxAttempts = 5 });
        audit.RecordFor(user.LastTenantId, user.Id, user.Email, "PasswordResetRequested", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim()) ?? throw InvalidLinkToken();
        var result = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                throw InvalidLinkToken();
            }

            result.EnsureSucceeded();
        }

        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        await RevokeAllSessionsAsync(user.Id, exceptSessionId: null, "password_reset", cancellationToken);
        audit.RecordFor(user.LastTenantId, user.Id, user.Email, "PasswordReset", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<AuthResult> AcceptInvitationAsync(AcceptInvitationRequest request, ClientInfo client, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim()) ?? throw InvalidLinkToken();
        if (!user.IsActive || await userManager.HasPasswordAsync(user) ||
            !await userManager.VerifyUserTokenAsync(user, InvitationTokenProvider.ProviderName, InvitationTokenProvider.Purpose, request.Token))
        {
            throw InvalidLinkToken();
        }

        (await userManager.AddPasswordAsync(user, request.Password)).EnsureSucceeded();
        user.EmailConfirmed = true;
        (await userManager.UpdateAsync(user)).EnsureSucceeded();

        var tenantIds = await ActiveTenantIdsAsync(user.Id, cancellationToken);
        if (tenantIds.Count == 0)
        {
            throw new AuthenticationFailedException("no_active_membership", "Your account is not active in any organization.");
        }

        audit.RecordFor(tenantIds[0], user.Id, user.Email, "InvitationAccepted", "User", user.Id.ToString());
        return await IssueSessionAsync(user, tenantIds[0], client, cancellationToken);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = scope.UserId ?? throw new ForbiddenException("Authentication is required.");
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException("Authentication is required.");
        var result = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                throw new ValidationException([new ValidationFailure(nameof(request.CurrentPassword), "The current password is incorrect.")]);
            }

            result.EnsureSucceeded();
        }

        await RevokeAllSessionsAsync(user.Id, scope.SessionId, "password_changed", cancellationToken);
        audit.Record("PasswordChanged", "User", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = scope.UserId ?? throw new ForbiddenException("Authentication is required.");
        var sessionId = scope.SessionId ?? throw new ForbiddenException("Authentication is required.");
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new ForbiddenException("Authentication is required.");
        var snapshot = await snapshots.GetAsync(sessionId, userId, scope.TenantId, cancellationToken) ?? throw SessionExpired();
        return await BuildCurrentUserAsync(user, snapshot, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken)
    {
        var userId = scope.UserId ?? throw new ForbiddenException("Authentication is required.");
        var now = clock.GetUtcNow();
        var sessions = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);
        var tenantIds = sessions.Where(s => s.TenantId.HasValue).Select(s => s.TenantId!.Value).Distinct().ToList();
        var names = await db.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return sessions.Select(s => new SessionDto(s.Id, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.IpAddress, s.UserAgent,
            s.TenantId is { } t ? names.GetValueOrDefault(t) : null, s.Id == scope.SessionId)).ToList();
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var userId = scope.UserId ?? throw new ForbiddenException("Authentication is required.");
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, cancellationToken)
                      ?? throw new NotFoundException("Session", sessionId);
        await RevokeSessionAsync(session, "revoked_by_user", cancellationToken);
        audit.Record("SessionRevoked", nameof(UserSession), sessionId.ToString());
        await db.SaveChangesAsync(cancellationToken);
    }

    // ---------- helpers ----------

    private async Task<AuthResult> IssueSessionAsync(ApplicationUser user, Guid? tenantId, ClientInfo client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var jwt = jwtOptions.Value;
        var session = new UserSession(user.Id, tenantId, client.IpAddress, client.UserAgent, now, now.AddDays(jwt.RefreshTokenDays));
        var (refreshToken, entity) = RefreshToken.Issue(session.Id, user.Id, now, session.ExpiresAt, client.IpAddress);
        db.UserSessions.Add(session);
        db.RefreshTokens.Add(entity);

        if (db.Entry(user).State == EntityState.Detached)
        {
            db.Users.Attach(user);
        }

        user.LastLoginAt = now;
        user.LastTenantId = tenantId ?? user.LastTenantId;
        audit.RecordFor(tenantId, user.Id, user.Email, "SignedIn", nameof(UserSession), session.Id.ToString(), new { client.IpAddress });
        await db.SaveChangesAsync(cancellationToken);
        return await BuildResultAsync(user, session, refreshToken, cancellationToken);
    }

    private async Task<AuthResult> BuildResultAsync(ApplicationUser user, UserSession session, string refreshToken, CancellationToken cancellationToken)
    {
        await snapshots.InvalidateSessionAsync(session.Id, cancellationToken);
        var snapshot = await snapshots.GetAsync(session.Id, user.Id, session.TenantId, cancellationToken) ?? throw SessionExpired();
        var access = tokens.CreateAccessToken(user, session.Id, session.TenantId, snapshot.Roles, snapshot.IsPlatformAdmin);
        return new AuthResult(access.Token, access.ExpiresAt, refreshToken, session.ExpiresAt,
            await BuildCurrentUserAsync(user, snapshot, cancellationToken));
    }

    private async Task<CurrentUserDto> BuildCurrentUserAsync(ApplicationUser user, MembershipSnapshot snapshot, CancellationToken cancellationToken)
    {
        var memberships = await (
                from m in db.TenantMemberships.IgnoreQueryFilters()
                join t in db.Tenants on m.TenantId equals t.Id
                where m.UserId == user.Id && m.IsActive && t.Status == TenantStatus.Active
                orderby t.Name
                select new TenantInfoDto(t.Id, t.Name, t.Slug))
            .ToListAsync(cancellationToken);
        var tenant = snapshot.TenantId is { } tenantId ? memberships.FirstOrDefault(m => m.Id == tenantId) : null;
        return new CurrentUserDto(user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName, snapshot.IsPlatformAdmin,
            tenant, snapshot.Roles, snapshot.Permissions, snapshot.WarehouseIds, memberships);
    }

    private async Task<List<Guid>> ActiveTenantIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await (
                from m in db.TenantMemberships.IgnoreQueryFilters()
                join t in db.Tenants on m.TenantId equals t.Id
                where m.UserId == userId && m.IsActive && t.Status == TenantStatus.Active
                orderby t.Name
                select m.TenantId)
            .ToListAsync(cancellationToken);

    private async Task RevokeSessionAsync(UserSession session, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        session.Revoke(now, reason);
        await db.RefreshTokens
            .Where(t => t.SessionId == session.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokedReason, reason), cancellationToken);
        await snapshots.InvalidateSessionAsync(session.Id, cancellationToken);
    }

    internal async Task RevokeAllSessionsAsync(Guid userId, Guid? exceptSessionId, string reason, CancellationToken cancellationToken)
    {
        var sessions = await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSessionId)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            await RevokeSessionAsync(session, reason, cancellationToken);
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private static AuthenticationFailedException InvalidCredentials() =>
        new("invalid_credentials", "The email or password is incorrect.");

    private static AuthenticationFailedException AccountLocked() =>
        new("account_locked", "This account is temporarily locked after repeated failed sign-in attempts. Try again later or ask an administrator to unlock it.");

    private static AuthenticationFailedException InvalidRefreshToken() =>
        new("invalid_refresh_token", "Your session is no longer valid. Please sign in again.");

    private static AuthenticationFailedException SessionExpired() =>
        new("session_expired", "Your session has expired. Please sign in again.");

    private static ValidationException InvalidLinkToken() =>
        new([new ValidationFailure("Token", "This link is invalid or has expired.")]);
}

/// <summary>Longer-lived token provider for invitations (3 days instead of the 2-hour reset window).</summary>
public sealed class InvitationTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    IOptions<InvitationTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger)
{
    public const string ProviderName = "Invitation";
    public const string Purpose = "AcceptInvitation";
}

public sealed class InvitationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public InvitationTokenProviderOptions()
    {
        Name = InvitationTokenProvider.ProviderName;
        TokenLifespan = TimeSpan.FromDays(3);
    }
}
