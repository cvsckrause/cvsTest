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
    public void Registered_Mandant_waits_for_approval_and_activates_once()
    {
        var now = DateTimeOffset.UnixEpoch;
        var mandant = Mandant.Register("Nordlicht GmbH", "hash", now.AddDays(7), now);

        Assert.Equal(MandantStatus.Pending, mandant.Status);
        Assert.Null(mandant.ActivatedAt);

        mandant.Activate(now.AddDays(1));
        mandant.Activate(now.AddDays(2));

        Assert.Equal(MandantStatus.Active, mandant.Status);
        Assert.Equal(now.AddDays(1), mandant.ActivatedAt);
    }

    [Fact]
    public void Expired_registration_cannot_be_activated()
    {
        var now = DateTimeOffset.UnixEpoch;
        var mandant = Mandant.Register("Nordlicht GmbH", "hash", now.AddDays(7), now);

        Assert.True(mandant.ApprovalExpired(now.AddDays(7)));
        Assert.Throws<InvalidOperationException>(() => mandant.Activate(now.AddDays(8)));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("Name <info@example.com>")]
    public void User_email_must_be_a_plain_address(string email)
    {
        Assert.Throws<ArgumentException>(() => MandantUser.Create(Tenant, "kunde", email));
    }

    [Fact]
    public void Login_name_combines_Mandant_number_and_user()
    {
        Assert.Equal("1001.kunde", MandantUser.Create(Tenant, "kunde", "info@example.com").LoginName(1001));
    }

    [Fact]
    public void User_names_are_stored_lower_case()
    {
        var user = MandantUser.Create(Tenant, " Kunde ", "info@example.com");

        Assert.Equal(MandantUser.FirstUserName, user.UserName);
    }
}
