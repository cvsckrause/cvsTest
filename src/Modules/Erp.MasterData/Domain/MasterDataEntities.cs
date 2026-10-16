using Erp.BuildingBlocks;

namespace Erp.MasterData.Domain;

public sealed class Customer : ITenantEntity
{
    private Customer()
    {
    }

    private Customer(Guid tenantId, string number, string name)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(number)) throw new ArgumentException("Customer number is required.", nameof(number));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Number = number.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public static Customer Create(Guid tenantId, string number, string name) => new(tenantId, number, name);
}

public enum AddressRole
{
    Billing = 0,
    Shipping = 1
}

public sealed class Address : ITenantEntity
{
    private Address()
    {
    }

    private Address(
        Guid tenantId,
        string name,
        string street,
        string postalCode,
        string city,
        string countryCode,
        string? additionalLine)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        Validate(name, street, postalCode, city, countryCode);

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Apply(name, street, postalCode, city, countryCode, additionalLine);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Street { get; private set; } = string.Empty;
    public string PostalCode { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string CountryCode { get; private set; } = string.Empty;
    public string? AdditionalLine { get; private set; }

    public static Address Create(
        Guid tenantId,
        string name,
        string street,
        string postalCode,
        string city,
        string countryCode = "DE",
        string? additionalLine = null) =>
        new(tenantId, name, street, postalCode, city, countryCode, additionalLine);

    public void Update(
        string name,
        string street,
        string postalCode,
        string city,
        string countryCode,
        string? additionalLine)
    {
        Validate(name, street, postalCode, city, countryCode);
        Apply(name, street, postalCode, city, countryCode, additionalLine);
    }

    public AddressSnapshot ToSnapshot() =>
        new(Name, Street, PostalCode, City, CountryCode, AdditionalLine);

    private void Apply(
        string name,
        string street,
        string postalCode,
        string city,
        string countryCode,
        string? additionalLine)
    {
        Name = name.Trim();
        Street = street.Trim();
        PostalCode = postalCode.Trim().ToUpperInvariant();
        City = city.Trim();
        CountryCode = countryCode.Trim().ToUpperInvariant();
        AdditionalLine = string.IsNullOrWhiteSpace(additionalLine) ? null : additionalLine.Trim();
    }

    private static void Validate(
        string name,
        string street,
        string postalCode,
        string city,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Address name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(street)) throw new ArgumentException("Street is required.", nameof(street));
        if (string.IsNullOrWhiteSpace(postalCode)) throw new ArgumentException("Postal code is required.", nameof(postalCode));
        if (string.IsNullOrWhiteSpace(city)) throw new ArgumentException("City is required.", nameof(city));
        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Trim().Length != 2)
        {
            throw new ArgumentException("Country code must be a 2-letter ISO code.", nameof(countryCode));
        }
    }
}

public sealed class CustomerAddress : ITenantEntity
{
    private CustomerAddress()
    {
    }

    private CustomerAddress(Guid tenantId, Guid customerId, Guid addressId, AddressRole role)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (customerId == Guid.Empty) throw new ArgumentException("Customer is required.", nameof(customerId));
        if (addressId == Guid.Empty) throw new ArgumentException("Address is required.", nameof(addressId));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        CustomerId = customerId;
        AddressId = addressId;
        Role = role;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid AddressId { get; private set; }
    public AddressRole Role { get; private set; }

    public static CustomerAddress Link(Guid tenantId, Guid customerId, Guid addressId, AddressRole role) =>
        new(tenantId, customerId, addressId, role);
}

public sealed class Article : ITenantEntity
{
    private Article()
    {
    }

    private Article(Guid tenantId, string sku, string name, decimal salesPrice, TaxCode taxCode)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(sku)) throw new ArgumentException("SKU is required.", nameof(sku));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Article name is required.", nameof(name));
        if (salesPrice < 0) throw new ArgumentOutOfRangeException(nameof(salesPrice));
        TaxCodes.RequireDefined(taxCode, nameof(taxCode));

        Id = Guid.NewGuid();
        TenantId = tenantId;
        Sku = sku.Trim().ToUpperInvariant();
        Name = name.Trim();
        SalesPrice = salesPrice;
        TaxCode = taxCode;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public decimal SalesPrice { get; private set; }
    public TaxCode TaxCode { get; private set; }

    public static Article Create(Guid tenantId, string sku, string name, decimal salesPrice, TaxCode taxCode = TaxCode.Standard19) =>
        new(tenantId, sku, name, salesPrice, taxCode);

    public void Update(string name, decimal salesPrice, TaxCode taxCode)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Article name is required.", nameof(name));
        if (salesPrice < 0) throw new ArgumentOutOfRangeException(nameof(salesPrice));
        TaxCodes.RequireDefined(taxCode, nameof(taxCode));

        Name = name.Trim();
        SalesPrice = salesPrice;
        TaxCode = taxCode;
    }
}
