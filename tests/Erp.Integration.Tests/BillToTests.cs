using System.Net;
using System.Text.Json;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class BillToTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Several_billing_addresses_need_a_choice_and_the_chosen_one_is_frozen()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var customer = await CustomerAsync(api, "Zwei Rechnungen GmbH");
        var shipTo = await LinkAsync(api, customer, "Lager", "Shipping");
        var b1 = await LinkAsync(api, customer, "Buchhaltung Nord", "Billing");
        var b2 = await LinkAsync(api, customer, "Buchhaltung Süd", "Billing");
        var article = await ArticleAsync(api);

        var unchosen = await CreateOrderAsync(api, customer.Id, shipTo, article, billToAddressId: null);
        Assert.Equal(HttpStatusCode.BadRequest, unchosen.Status);
        Assert.Contains("billToAddressId", unchosen.Body);

        var chosen = await CreateOrderAsync(api, customer.Id, shipTo, article, b2);
        Assert.Equal(HttpStatusCode.Created, chosen.Status);
        var moved = await api.PutAsync($"/api/v1/addresses/{b2}", new
        {
            name = "Buchhaltung Süd",
            street = "Umgezogen 99",
            postalCode = "80331",
            city = "München",
            countryCode = "DE"
        });
        Assert.Equal(HttpStatusCode.OK, moved.Status);

        var read = await api.GetAsync($"/api/v1/sales-orders/{chosen.Id}");
        var billTo = BillToOf(read.Body);
        Assert.Equal(("Zwei Rechnungen GmbH", customer.Number, b2), (billTo.BuyerName, billTo.BuyerNumber, billTo.AddressId));
        Assert.Equal(("Buchhaltung Süd", "2 Billing Street"), (billTo.Name, billTo.Street));
        Assert.NotEqual(b1, billTo.AddressId);
    }

    [Fact]
    public async Task A_single_billing_address_is_used_without_a_choice()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var customer = await CustomerAsync(api, "Eine Rechnung GmbH");
        var shipTo = await LinkAsync(api, customer, "Lager", "Shipping");
        var billing = await LinkAsync(api, customer, "Buchhaltung", "Billing");
        var article = await ArticleAsync(api);

        var created = await CreateOrderAsync(api, customer.Id, shipTo, article, billToAddressId: null);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        var billTo = BillToOf(created.Body);
        Assert.Equal((billing, "Buchhaltung"), (billTo.AddressId, billTo.Name));
    }

    [Fact]
    public async Task Without_a_billing_address_the_ship_to_is_billed_under_the_customer_name()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var customer = await CustomerAsync(api, "Nur Lieferung GmbH");
        var shipTo = await LinkAsync(api, customer, "Lager Ost", "Shipping");
        var article = await ArticleAsync(api);

        var created = await CreateOrderAsync(api, customer.Id, shipTo, article, billToAddressId: null);

        Assert.Equal(HttpStatusCode.Created, created.Status);
        var billTo = BillToOf(created.Body);
        Assert.Equal(("Nur Lieferung GmbH", customer.Number, (Guid?)null), (billTo.BuyerName, billTo.BuyerNumber, billTo.AddressId));
        Assert.Equal(("Lager Ost", "2 Shipping Street"), (billTo.Name, billTo.Street));
    }

    [Fact]
    public async Task A_bill_to_linked_only_for_shipping_is_rejected()
    {
        using var api = fixture.OpenSession(fixture.TenantA);
        var customer = await CustomerAsync(api, "Falsche Rolle GmbH");
        var shipTo = await LinkAsync(api, customer, "Lager", "Shipping");
        var article = await ArticleAsync(api);

        var created = await CreateOrderAsync(api, customer.Id, shipTo, article, shipTo);

        Assert.Equal(HttpStatusCode.BadRequest, created.Status);
        Assert.Contains("Billing", created.Body);
    }

    [Fact]
    public async Task Another_tenants_billing_address_is_rejected_without_revealing_it()
    {
        using var apiA = fixture.OpenSession(fixture.TenantA);
        using var apiB = fixture.OpenSession(fixture.TenantB);
        var customerA = await CustomerAsync(apiA, "Fremdmandant GmbH");
        var billingA = await LinkAsync(apiA, customerA, "Geheime Buchhaltung", "Billing");
        var customerB = await CustomerAsync(apiB, "Eigener Kunde GmbH");
        var shipToB = await LinkAsync(apiB, customerB, "Lager B", "Shipping");
        var articleB = await ArticleAsync(apiB);

        var created = await CreateOrderAsync(apiB, customerB.Id, shipToB, articleB, billingA);

        Assert.Equal(HttpStatusCode.BadRequest, created.Status);
        Assert.Contains("Billing", created.Body);
        Assert.DoesNotContain("Geheime Buchhaltung", created.Body);
    }

    private static async Task<(Guid Id, string Number)> CustomerAsync(ApiSession api, string name)
    {
        var number = $"BT-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var customer = await api.PostOkAsync("/api/v1/customers", new { number, name });
        return (customer.Id, number);
    }

    private static async Task<Guid> LinkAsync(ApiSession api, (Guid Id, string Number) customer, string name, string role)
    {
        var address = await api.PostOkAsync("/api/v1/addresses", new
        {
            name,
            street = $"2 {role} Street",
            postalCode = "10115",
            city = "Berlin",
            countryCode = "DE"
        });
        await api.PostOkAsync($"/api/v1/customers/{customer.Id}/addresses", new { addressId = address.Id, role });
        return address.Id;
    }

    private static async Task<Guid> ArticleAsync(ApiSession api)
    {
        var article = await api.PostOkAsync("/api/v1/articles", new
        {
            sku = $"BT-{Guid.NewGuid():N}"[..16],
            name = "Rechnungsartikel",
            salesPrice = 5m,
            openingStock = 5m
        });
        return article.Id;
    }

    private static Task<ApiResult> CreateOrderAsync(ApiSession api, Guid customerId, Guid shipTo, Guid article, Guid? billToAddressId) =>
        api.PostAsync(
            "/api/v1/sales-orders",
            new
            {
                customerId,
                shipToAddressId = shipTo,
                billToAddressId,
                lines = new[] { new { articleId = article, quantity = 1m } }
            },
            $"bt-{Guid.NewGuid():N}");

    private static (string BuyerName, string BuyerNumber, Guid? AddressId, string Name, string Street) BillToOf(string body)
    {
        using var document = JsonDocument.Parse(body);
        var billTo = document.RootElement.GetProperty("billTo");
        var address = billTo.GetProperty("address");
        var addressId = billTo.GetProperty("addressId");
        return (
            billTo.GetProperty("buyerName").GetString()!,
            billTo.GetProperty("buyerNumber").GetString()!,
            addressId.ValueKind == JsonValueKind.Null ? null : addressId.GetGuid(),
            address.GetProperty("name").GetString()!,
            address.GetProperty("street").GetString()!);
    }
}
