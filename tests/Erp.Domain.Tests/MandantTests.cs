using Erp.BuildingBlocks;
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
    public void New_Mandant_lists_every_missing_seller_field()
    {
        var mandant = Mandant.Seed(Tenant, 100, "Demo", DateTimeOffset.UnixEpoch);

        Assert.Equal(
            ["street", "postalCode", "city", "countryCode", "steuernummerOrUstIdNr"],
            mandant.MissingSellerFields());
        Assert.Equal(Mandant.SellerFields, mandant.MissingSellerFields());
    }

    [Fact]
    public void Seller_snapshot_needs_complete_details_and_carries_the_Firmenname()
    {
        var mandant = Mandant.Seed(Tenant, 100, "Northstar Demo GmbH", DateTimeOffset.UnixEpoch);
        Assert.Throws<InvalidOperationException>(() => mandant.ToSellerSnapshot());

        mandant.ChangeSellerDetails("Hafenstraße 1", "20095", "Hamburg", "DE", "Haus B", null, "DE123456789");

        Assert.Equal(
            new SellerSnapshot("Northstar Demo GmbH", "Hafenstraße 1", "20095", "Hamburg", "DE", "Haus B", null, "DE123456789"),
            mandant.ToSellerSnapshot());
    }

    [Fact]
    public void Seller_details_with_a_Steuernummer_only_are_complete()
    {
        var mandant = Mandant.Seed(Tenant, 100, "Demo", DateTimeOffset.UnixEpoch);

        mandant.ChangeSellerDetails(" Hafenstraße 1 ", "20095", "Hamburg", "de", " ", "  12/345/67890 ", null);

        Assert.Empty(mandant.MissingSellerFields());
        Assert.Equal("Hafenstraße 1", mandant.SellerStreet);
        Assert.Equal("DE", mandant.SellerCountryCode);
        Assert.Null(mandant.SellerAdditionalLine);
        Assert.Equal("12/345/67890", mandant.Steuernummer);
        Assert.Null(mandant.UstIdNr);
    }

    [Fact]
    public void UstIdNr_is_stored_upper_case_without_spaces()
    {
        var mandant = Mandant.Seed(Tenant, 100, "Demo", DateTimeOffset.UnixEpoch);

        mandant.ChangeSellerDetails("Hafenstraße 1", "20095", "Hamburg", "DE", null, null, "de 123 456 789");

        Assert.Equal("DE123456789", mandant.UstIdNr);
        Assert.Null(mandant.Steuernummer);
        Assert.Empty(mandant.MissingSellerFields());
    }

    [Theory]
    [InlineData("", "20095", "Hamburg", "DE", "12/345/67890", null)]
    [InlineData("Hafenstraße 1", " ", "Hamburg", "DE", "12/345/67890", null)]
    [InlineData("Hafenstraße 1", "20095", "", "DE", "12/345/67890", null)]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "D", "12/345/67890", null)]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "DE", null, null)]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "DE", " ", " ")]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "DE", null, "D1")]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "DE", null, "DE12345678901234")]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "DE", null, "1E123456789")]
    [InlineData("Hafenstraße 1", "20095", "Hamburg", "DE", null, "DE-123456789")]
    public void Incomplete_or_malformed_seller_details_are_rejected(
        string street, string postalCode, string city, string countryCode, string? steuernummer, string? ustIdNr)
    {
        var mandant = Mandant.Seed(Tenant, 100, "Demo", DateTimeOffset.UnixEpoch);

        Assert.Throws<ArgumentException>(() =>
            mandant.ChangeSellerDetails(street, postalCode, city, countryCode, null, steuernummer, ustIdNr));
        Assert.NotEmpty(mandant.MissingSellerFields());
    }

    [Fact]
    public void User_names_are_stored_lower_case()
    {
        var user = MandantUser.Create(Tenant, " Kunde ", "info@example.com");

        Assert.Equal(MandantUser.FirstUserName, user.UserName);
    }
}
