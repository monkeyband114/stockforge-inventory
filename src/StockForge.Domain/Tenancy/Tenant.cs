using StockForge.Domain.Common;

namespace StockForge.Domain.Tenancy;

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
}

/// <summary>An organization using the platform. The root of every tenant-owned record.</summary>
public sealed class Tenant : AuditableEntity, IAuditable
{
    private Tenant() { }

    public Tenant(string name, string slug, string contactEmail, string baseCurrency, string timeZone)
    {
        Name = Guard.Required(name, "Organization name", 200);
        Slug = Guard.Required(slug, "Slug", 80).ToLowerInvariant();
        ContactEmail = Guard.Required(contactEmail, "Contact email", 256);
        BaseCurrency = Guard.Required(baseCurrency, "Base currency", 3).ToUpperInvariant();
        TimeZone = Guard.Required(timeZone, "Time zone", 64);
        Status = TenantStatus.Active;
        Settings = new TenantSettings();
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string ContactEmail { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public Address? Address { get; private set; }
    public string BaseCurrency { get; private set; } = "USD";
    public string TimeZone { get; private set; } = "UTC";
    public TenantStatus Status { get; private set; }
    public DateTimeOffset? SuspendedAt { get; private set; }
    public string? SuspensionReason { get; private set; }
    public TenantSettings Settings { get; private set; } = new();
    public uint Version { get; private set; }

    public bool IsActive => Status == TenantStatus.Active;

    public void UpdateProfile(string name, string contactEmail, string? phone, Address? address, string baseCurrency, string timeZone)
    {
        Name = Guard.Required(name, "Organization name", 200);
        ContactEmail = Guard.Required(contactEmail, "Contact email", 256);
        Phone = Guard.Optional(phone, "Phone", 40);
        Address = address;
        BaseCurrency = Guard.Required(baseCurrency, "Base currency", 3).ToUpperInvariant();
        TimeZone = Guard.Required(timeZone, "Time zone", 64);
    }

    public void UpdateSettings(TenantSettings settings) => Settings = settings;

    public void Suspend(string reason, DateTimeOffset at)
    {
        if (Status == TenantStatus.Suspended)
        {
            throw new InvalidStateTransitionException(nameof(Tenant), Status.ToString(), TenantStatus.Suspended.ToString());
        }

        Status = TenantStatus.Suspended;
        SuspendedAt = at;
        SuspensionReason = Guard.Required(reason, "Suspension reason", 500);
    }

    public void Activate()
    {
        if (Status == TenantStatus.Active)
        {
            throw new InvalidStateTransitionException(nameof(Tenant), Status.ToString(), TenantStatus.Active.ToString());
        }

        Status = TenantStatus.Active;
        SuspendedAt = null;
        SuspensionReason = null;
    }
}

/// <summary>Organization-configurable business rules (approval thresholds, tax defaults, numbering).</summary>
public sealed class TenantSettings
{
    public TenantSettings() { }

    public TenantSettings(
        decimal orderApprovalAmountThreshold,
        decimal orderApprovalQuantityThreshold,
        bool requireApprovalForAllOrders,
        decimal defaultTaxRate,
        int invoiceDueDays,
        bool stockAdjustmentRequiresApproval,
        bool lowStockAlertsEnabled,
        decimal purchaseApprovalThreshold = 5_000m)
    {
        OrderApprovalAmountThreshold = Guard.NotNegative(orderApprovalAmountThreshold, "Order approval amount threshold");
        OrderApprovalQuantityThreshold = Guard.NotNegative(orderApprovalQuantityThreshold, "Order approval quantity threshold");
        RequireApprovalForAllOrders = requireApprovalForAllOrders;
        if (defaultTaxRate is < 0 or > 1)
        {
            throw new DomainValidationException("Default tax rate must be between 0 and 1 (e.g. 0.075 for 7.5%).");
        }

        DefaultTaxRate = defaultTaxRate;
        InvoiceDueDays = invoiceDueDays is >= 0 and <= 365
            ? invoiceDueDays
            : throw new DomainValidationException("Invoice due days must be between 0 and 365.");
        StockAdjustmentRequiresApproval = stockAdjustmentRequiresApproval;
        LowStockAlertsEnabled = lowStockAlertsEnabled;
        PurchaseApprovalThreshold = Guard.NotNegative(purchaseApprovalThreshold, "Purchase approval threshold");
    }

    /// <summary>Orders whose total exceeds this amount require manager approval.</summary>
    public decimal OrderApprovalAmountThreshold { get; private set; } = 10_000m;

    /// <summary>Orders whose total quantity exceeds this value (bulk orders) require manager approval.</summary>
    public decimal OrderApprovalQuantityThreshold { get; private set; } = 500m;

    public bool RequireApprovalForAllOrders { get; private set; }

    /// <summary>Fractional tax rate applied when a product has no explicit rate (0.075 = 7.5%).</summary>
    public decimal DefaultTaxRate { get; private set; }

    public int InvoiceDueDays { get; private set; } = 30;

    public bool StockAdjustmentRequiresApproval { get; private set; } = true;

    public bool LowStockAlertsEnabled { get; private set; } = true;

    /// <summary>Purchase orders with a total above this need approval; 0 means every purchase order needs approval.</summary>
    public decimal PurchaseApprovalThreshold { get; private set; } = 5_000m;
}
