namespace Erp.Platform.Domain;

public sealed class TenantMembership
{
    private TenantMembership()
    {
    }

    private TenantMembership(string userId, Guid tenantId, bool isDefault)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TenantId = tenantId;
        IsDefault = isDefault;
    }

    public Guid Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public Guid TenantId { get; private set; }
    public bool IsDefault { get; private set; }

    public static TenantMembership Grant(string userId, Guid tenantId, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("User is required.", nameof(userId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        return new TenantMembership(userId.Trim(), tenantId, isDefault);
    }

    public static IReadOnlyList<TenantMembership> ParseSeed(string text)
    {
        var grants = new List<TenantMembership>();
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length is < 2 or > 3)
            {
                throw new FormatException($"Membership seed '{entry}' must be user:tenant or user:tenant:default.");
            }

            if (!Guid.TryParse(parts[1], out var tenantId))
            {
                throw new FormatException($"Membership seed '{entry}' has an invalid tenant id.");
            }

            var isDefault = false;
            if (parts.Length == 3)
            {
                if (!parts[2].Equals("default", StringComparison.OrdinalIgnoreCase))
                {
                    throw new FormatException($"Membership seed '{entry}' has an unexpected flag '{parts[2]}'.");
                }

                isDefault = true;
            }

            grants.Add(Grant(parts[0], tenantId, isDefault));
        }

        return grants;
    }
}
