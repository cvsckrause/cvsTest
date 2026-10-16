using Erp.Platform.Domain;

namespace Erp.Domain.Tests;

public sealed class MandantTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Seeded_Mandant_is_active_and_labelled()
    {
        var mandant = Mandant.Seed(Tenant, 100, "  Northstar Demo GmbH ", DateTimeOffset.UnixEpoch);

        Assert.Equal(MandantStatus.Active, mandant.Status);
        Assert.Equal("Mandant 100 · Northstar Demo GmbH", mandant.Label);
        Assert.Equal(DateTimeOffset.UnixEpoch, mandant.ActivatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Seeded_numbers_stay_below_the_registration_sequence(long number)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Mandant.Seed(Tenant, number, "Demo", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Firmenname_is_required()
    {
        Assert.Throws<ArgumentException>(() => Mandant.Seed(Tenant, 100, "   ", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void User_names_are_stored_lower_case()
    {
        var user = MandantUser.Create(Tenant, " Kunde ", "info@example.com");

        Assert.Equal(MandantUser.FirstUserName, user.UserName);
    }
}
