using StockForge.Domain.Common;

namespace StockForge.Domain.Inventory;

public enum WarehouseType
{
    DistributionCenter = 1,
    Warehouse = 2,
    RetailStore = 3,
    ThirdPartyLogistics = 4,
}

public sealed class Warehouse : TenantEntity, IAuditable
{
    private Warehouse() { }

    public Warehouse(string code, string name, WarehouseType type, Address? address, string? phone, string? email)
    {
        Code = NormalizeCode(code);
        Update(name, type, address, phone, email);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public WarehouseType Type { get; private set; }
    public Address? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public bool IsActive { get; private set; }
    public uint Version { get; private set; }

    public void Update(string name, WarehouseType type, Address? address, string? phone, string? email)
    {
        Name = Guard.Required(name, "Warehouse name", 120);
        Type = Enum.IsDefined(type) ? type : throw new DomainValidationException("Unknown warehouse type.");
        Address = address;
        Phone = Guard.Optional(phone, "Phone", 40);
        Email = Guard.Optional(email, "Email", 256);
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    public void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainException("warehouse_inactive", $"Warehouse '{Code}' is inactive.");
        }
    }

    private static string NormalizeCode(string code)
    {
        var normalized = Guard.Required(code, "Warehouse code", 20).ToUpperInvariant();
        return normalized.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? normalized
            : throw new DomainValidationException("Warehouse code may contain letters, digits and '-'.");
    }
}
