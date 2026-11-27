using FluentValidation;
using StockForge.Application.Common;
using StockForge.Application.Organization;
using StockForge.Application.Security;

namespace StockForge.Application.Identity;

// ---------- Authentication ----------

public sealed record ClientInfo(string? IpAddress, string? UserAgent);

public sealed record RegisterOrganizationRequest(
    string OrganizationName,
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string? BaseCurrency,
    string? TimeZone);

public sealed record LoginRequest(string Email, string Password, Guid? TenantId);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record SwitchTenantRequest(Guid TenantId);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record AcceptInvitationRequest(string Email, string Token, string Password);

public sealed record TenantInfoDto(Guid Id, string Name, string Slug);

public sealed record CurrentUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    bool IsPlatformAdmin,
    TenantInfoDto? Tenant,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<Guid>? WarehouseIds,
    IReadOnlyList<TenantInfoDto> Memberships);

/// <summary>Result of a successful sign-in. The refresh token is also set as an HttpOnly cookie by the API.</summary>
public sealed record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserDto User);

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent,
    string? TenantName,
    bool IsCurrent);

public interface IAuthService
{
    Task<AuthResult> RegisterOrganizationAsync(RegisterOrganizationRequest request, ClientInfo client, CancellationToken cancellationToken);
    Task<AuthResult> LoginAsync(LoginRequest request, ClientInfo client, CancellationToken cancellationToken);
    Task<AuthResult> RefreshAsync(string refreshToken, ClientInfo client, CancellationToken cancellationToken);
    Task<AuthResult> SwitchTenantAsync(Guid tenantId, ClientInfo client, CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
    Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);
    Task<AuthResult> AcceptInvitationAsync(AcceptInvitationRequest request, ClientInfo client, CancellationToken cancellationToken);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken);
    Task<CurrentUserDto> GetCurrentUserAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<SessionDto>> GetSessionsAsync(CancellationToken cancellationToken);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}

// ---------- Organization members ----------

public sealed record MemberDto(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string? JobTitle,
    bool IsActive,
    bool IsLockedOut,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> GrantedPermissions,
    IReadOnlyList<Guid> WarehouseIds,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LastLoginAt);

public sealed record MemberQuery : PageQuery
{
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record InviteMemberRequest(
    string Email,
    string FirstName,
    string LastName,
    string? JobTitle,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid>? WarehouseIds);

public sealed record UpdateMemberRequest(
    string? JobTitle,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> WarehouseIds,
    IReadOnlyList<string> GrantedPermissions);

public sealed record RoleDto(string Name, string? Description, IReadOnlyList<string> Permissions);

public interface IUserManagementService
{
    Task<PagedResult<MemberDto>> ListAsync(MemberQuery query, CancellationToken cancellationToken);
    Task<MemberDto> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<MemberDto> InviteAsync(InviteMemberRequest request, CancellationToken cancellationToken);
    Task<MemberDto> UpdateAsync(Guid userId, UpdateMemberRequest request, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid userId, CancellationToken cancellationToken);
    Task ActivateAsync(Guid userId, CancellationToken cancellationToken);
    Task UnlockAsync(Guid userId, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken cancellationToken);
}

// ---------- Platform onboarding ----------

public sealed record CreateOrganizationRequest(
    string OrganizationName,
    string AdminEmail,
    string AdminFirstName,
    string AdminLastName,
    string? BaseCurrency,
    string? TimeZone);

public sealed record CreatedOrganizationDto(Guid TenantId, string Name, string Slug, Guid AdminUserId, bool AdminInvited);

public interface IOnboardingService
{
    /// <summary>Platform-admin flow: creates an organization and invites its first administrator by email.</summary>
    Task<CreatedOrganizationDto> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken);
}

// ---------- Validators ----------

internal static class PasswordRules
{
    public const int MinLength = 10;

    public static IRuleBuilderOptions<T, string> StrongPassword<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(MinLength).WithMessage($"Password must be at least {MinLength} characters.")
            .MaximumLength(128)
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
}

public sealed class RegisterOrganizationRequestValidator : AbstractValidator<RegisterOrganizationRequest>
{
    public RegisterOrganizationRequestValidator()
    {
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).StrongPassword();
        RuleFor(x => x.BaseCurrency).Length(3).Matches("^[A-Za-z]{3}$").When(x => !string.IsNullOrEmpty(x.BaseCurrency));
        RuleFor(x => x.TimeZone).Must(TimeZones.IsValid).When(x => !string.IsNullOrEmpty(x.TimeZone)).WithMessage("Unknown time zone.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.NewPassword).StrongPassword();
    }
}

public sealed class AcceptInvitationRequestValidator : AbstractValidator<AcceptInvitationRequest>
{
    public AcceptInvitationRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Token).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.Password).StrongPassword();
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).StrongPassword().NotEqual(x => x.CurrentPassword)
            .WithMessage("The new password must differ from the current one.");
    }
}

public sealed class InviteMemberRequestValidator : AbstractValidator<InviteMemberRequest>
{
    public InviteMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.JobTitle).MaximumLength(120);
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).Must(RoleNames.IsTenantRole).WithMessage("'{PropertyValue}' is not an assignable role.");
    }
}

public sealed class UpdateMemberRequestValidator : AbstractValidator<UpdateMemberRequest>
{
    public UpdateMemberRequestValidator()
    {
        RuleFor(x => x.JobTitle).MaximumLength(120);
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles).Must(RoleNames.IsTenantRole).WithMessage("'{PropertyValue}' is not an assignable role.");
        RuleFor(x => x.WarehouseIds).NotNull();
        RuleFor(x => x.GrantedPermissions).NotNull();
        RuleForEach(x => x.GrantedPermissions)
            .Must(p => Permissions.IsKnown(p) && !p.StartsWith("platform.", StringComparison.Ordinal))
            .WithMessage("'{PropertyValue}' is not a grantable permission.");
    }
}

public sealed class CreateOrganizationRequestValidator : AbstractValidator<CreateOrganizationRequest>
{
    public CreateOrganizationRequestValidator()
    {
        RuleFor(x => x.OrganizationName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.AdminFirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AdminLastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.BaseCurrency).Length(3).When(x => !string.IsNullOrEmpty(x.BaseCurrency));
        RuleFor(x => x.TimeZone).Must(TimeZones.IsValid).When(x => !string.IsNullOrEmpty(x.TimeZone)).WithMessage("Unknown time zone.");
    }
}
