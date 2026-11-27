using StockForge.Domain.Common;

namespace StockForge.Domain.Sales;

public sealed class Customer : TenantEntity, IAuditable
{
    private Customer() { }

    public Customer(string code, string name, string? email, string? phone, string? taxNumber, Address? billingAddress,
        Address? shippingAddress, int? paymentTermsDays, decimal? creditLimit, string? notes)
    {
        Code = Guard.Required(code, "Customer code", 32).ToUpperInvariant();
        Update(name, email, phone, taxNumber, billingAddress, shippingAddress, paymentTermsDays, creditLimit, notes);
        IsActive = true;
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? TaxNumber { get; private set; }
    public Address? BillingAddress { get; private set; }
    public Address? ShippingAddress { get; private set; }

    /// <summary>Overrides the organization's invoice due days for this customer.</summary>
    public int? PaymentTermsDays { get; private set; }

    public decimal? CreditLimit { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; }
    public uint Version { get; private set; }

    public void Update(string name, string? email, string? phone, string? taxNumber, Address? billingAddress,
        Address? shippingAddress, int? paymentTermsDays, decimal? creditLimit, string? notes)
    {
        Name = Guard.Required(name, "Customer name", 200);
        Email = Guard.Optional(email, "Email", 256);
        Phone = Guard.Optional(phone, "Phone", 40);
        TaxNumber = Guard.Optional(taxNumber, "Tax number", 64);
        BillingAddress = billingAddress;
        ShippingAddress = shippingAddress;
        if (paymentTermsDays is < 0 or > 365)
        {
            throw new DomainValidationException("Payment terms must be between 0 and 365 days.");
        }

        PaymentTermsDays = paymentTermsDays;
        CreditLimit = creditLimit is { } limit ? Guard.NotNegative(limit, "Credit limit") : null;
        Notes = Guard.Optional(notes, "Notes", 2000);
    }

    public void SetActive(bool isActive) => IsActive = isActive;
}
