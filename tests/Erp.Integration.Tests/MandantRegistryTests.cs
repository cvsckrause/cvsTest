using System.Net;
using System.Text.Json;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MandantRegistryTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Session_returns_the_Mandant_of_the_selected_tenant()
    {
        using var api = fixture.OpenSession("integration-b", fixture.TenantB);

        var result = await api.GetAsync("/api/v1/session");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        using var body = JsonDocument.Parse(result.Body);
        Assert.Equal(fixture.TenantB, body.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Equal(PostgresFixture.MandantNumberB, body.RootElement.GetProperty("mandantNumber").GetInt64());
        Assert.Equal("B Vertrieb GmbH", body.RootElement.GetProperty("firmenname").GetString());
        Assert.Equal("integration-b", body.RootElement.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Membership_list_names_each_Mandant()
    {
        using var api = fixture.OpenSession("integration-a", tenantId: null);

        var result = await api.GetAsync("/api/v1/me/memberships");

        Assert.Equal(HttpStatusCode.OK, result.Status);
        using var body = JsonDocument.Parse(result.Body);
        var membership = Assert.Single(body.RootElement.GetProperty("memberships").EnumerateArray());
        Assert.Equal(fixture.TenantA, membership.GetProperty("tenantId").GetGuid());
        Assert.Equal(PostgresFixture.MandantNumberA, membership.GetProperty("mandantNumber").GetInt64());
        Assert.Equal("A Handels GmbH", membership.GetProperty("firmenname").GetString());
    }

    [Fact]
    public async Task Registered_Mandant_numbers_start_at_1001()
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "select start_value from pg_sequences where schemaname = 'erp' and sequencename = 'mandant_number_seq'",
            connection);

        Assert.Equal(1001L, (long)(await command.ExecuteScalarAsync() ?? 0L));
    }

    [Theory]
    [InlineData("erp_app", true)]
    [InlineData("erp_worker", false)]
    public async Task Only_the_api_role_reads_the_registry(string role, bool expected)
    {
        await using var connection = new NpgsqlConnection(fixture.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select has_table_privilege(@role, 'erp.mandanten', 'SELECT')
               and has_table_privilege(@role, 'erp.mandant_users', 'SELECT')
               and has_sequence_privilege(@role, 'erp.mandant_number_seq', 'USAGE')
            """,
            connection);
        command.Parameters.AddWithValue("role", role);

        Assert.Equal(expected, (bool)(await command.ExecuteScalarAsync() ?? false));
    }
}
