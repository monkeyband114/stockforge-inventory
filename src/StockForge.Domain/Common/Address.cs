namespace StockForge.Domain.Common;

/// <summary>Postal address value object (stored as owned columns).</summary>
public sealed class Address
{
    private Address() { }

    public Address(string line1, string? line2, string city, string? state, string? postalCode, string country)
    {
        Line1 = Guard.Required(line1, "Address line 1", 200);
        Line2 = Guard.Optional(line2, "Address line 2", 200);
        City = Guard.Required(city, "City", 100);
        State = Guard.Optional(state, "State", 100);
        PostalCode = Guard.Optional(postalCode, "Postal code", 20);
        Country = Guard.Required(country, "Country", 100);
    }

    public string Line1 { get; private set; } = string.Empty;
    public string? Line2 { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string? State { get; private set; }
    public string? PostalCode { get; private set; }
    public string Country { get; private set; } = string.Empty;

    /// <summary>A detached copy; owned value objects cannot be shared between two owners.</summary>
    public Address Copy() => new(Line1, Line2, City, State, PostalCode, Country);
}
