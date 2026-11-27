using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockForge.Application.Abstractions;
using StockForge.Application.Warehouses;
using StockForge.Domain.Tenancy;

namespace StockForge.Application.Organization;

public sealed record TenantSettingsDto(
    decimal OrderApprovalAmountThreshold,
    decimal OrderApprovalQuantityThreshold,
    bool RequireApprovalForAllOrders,
    decimal DefaultTaxRate,
    int InvoiceDueDays,
    bool StockAdjustmentRequiresApproval,
    bool LowStockAlertsEnabled,
    decimal PurchaseApprovalThreshold = 5_000m)
{
    public static TenantSettingsDto From(TenantSettings s) => new(
        s.OrderApprovalAmountThreshold, s.OrderApprovalQuantityThreshold, s.RequireApprovalForAllOrders,
        s.DefaultTaxRate, s.InvoiceDueDays, s.StockAdjustmentRequiresApproval, s.LowStockAlertsEnabled, s.PurchaseApprovalThreshold);

    public TenantSettings ToDomain() => new(
        OrderApprovalAmountThreshold, OrderApprovalQuantityThreshold, RequireApprovalForAllOrders, DefaultTaxRate,
        InvoiceDueDays, StockAdjustmentRequiresApproval, LowStockAlertsEnabled, PurchaseApprovalThreshold);
}

public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    string ContactEmail,
    string? Phone,
    AddressDto? Address,
    string BaseCurrency,
    string TimeZone,
    TenantStatus Status,
    TenantSettingsDto Settings,
    DateTimeOffset CreatedAt,
    uint Version);

public sealed record UpdateOrganizationRequest(
    string Name,
    string ContactEmail,
    string? Phone,
    AddressDto? Address,
    string BaseCurrency,
    string TimeZone,
    uint? Version);

public static class TimeZones
{
    public static bool IsValid(string? id) => !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}

public sealed class UpdateOrganizationRequestValidator : AbstractValidator<UpdateOrganizationRequest>
{
    public UpdateOrganizationRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ContactEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Phone).MaximumLength(40);
        RuleFor(x => x.Address!).SetValidator(new AddressDtoValidator()).When(x => x.Address is not null);
        RuleFor(x => x.BaseCurrency).NotEmpty().Length(3).Matches("^[A-Za-z]{3}$");
        RuleFor(x => x.TimeZone).Must(TimeZones.IsValid).WithMessage("Unknown time zone.");
    }
}

public sealed class TenantSettingsDtoValidator : AbstractValidator<TenantSettingsDto>
{
    public TenantSettingsDtoValidator()
    {
        RuleFor(x => x.OrderApprovalAmountThreshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.OrderApprovalQuantityThreshold).GreaterThanOrEqualTo(0);
        RuleFor(x => x.DefaultTaxRate).InclusiveBetween(0, 1);
        RuleFor(x => x.InvoiceDueDays).InclusiveBetween(0, 365);
        RuleFor(x => x.PurchaseApprovalThreshold).GreaterThanOrEqualTo(0);
    }
}

public sealed class OrganizationService(IAppDbContext db, ITenantContext tenantContext)
{
    public async Task<OrganizationDto> GetAsync(CancellationToken cancellationToken) =>
        ToDto(await LoadAsync(asTracking: false, cancellationToken));

    public async Task<OrganizationDto> UpdateAsync(UpdateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var tenant = await LoadAsync(asTracking: true, cancellationToken);
        WarehouseService.EnsureVersion(tenant.Version, request.Version);
        tenant.UpdateProfile(request.Name, request.ContactEmail, request.Phone, request.Address?.ToDomain(),
            request.BaseCurrency, request.TimeZone);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(tenant);
    }

    public async Task<OrganizationDto> UpdateSettingsAsync(TenantSettingsDto settings, CancellationToken cancellationToken)
    {
        var tenant = await LoadAsync(asTracking: true, cancellationToken);
        tenant.UpdateSettings(settings.ToDomain());
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(tenant);
    }

    private async Task<Tenant> LoadAsync(bool asTracking, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.RequiredTenantId;
        var query = asTracking ? db.Tenants : db.Tenants.AsNoTracking();
        return await query.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
               ?? throw new NotFoundException("Organization", tenantId);
    }

    private static OrganizationDto ToDto(Tenant t) => new(
        t.Id, t.Name, t.Slug, t.ContactEmail, t.Phone, AddressDto.From(t.Address), t.BaseCurrency, t.TimeZone,
        t.Status, TenantSettingsDto.From(t.Settings), t.CreatedAt, t.Version);
}
