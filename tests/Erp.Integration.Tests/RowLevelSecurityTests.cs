namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RowLevelSecurityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task App_role_cannot_run_ddl_or_read_another_tenant()
    {
        var role = await fixture.Probe.CurrentRoleAsync();
        Assert.Equal("erp_app", role.User);
        Assert.False(role.Superuser);
        Assert.False(role.BypassRls);
        Assert.Equal("42501", await fixture.Probe.CreateTableAsAppAsync());

        var leaks = new List<string>();
        var unseeded = new List<string>();
        var unprotected = new List<string>();
        foreach (var table in DatabaseProbe.TenantTables())
        {
            var isolation = await fixture.Probe.IsolationAsync(table);
            if (!isolation.Enabled || !isolation.Forced || !isolation.Policy)
            {
                unprotected.Add(table.Qualified);
            }

            var stored = await fixture.Probe.CountOwnerRowsAsync(table, fixture.TenantA);
            if (stored == 0)
            {
                unseeded.Add(table.Qualified);
            }

            var visible = await fixture.Probe.CountVisibleOwnerRowsAsync(fixture.TenantB, table, fixture.TenantA);
            if (visible != 0)
            {
                leaks.Add($"{table.Qualified} ({visible})");
            }
        }

        Assert.True(unprotected.Count == 0, "Missing forced tenant_isolation on: " + string.Join(", ", unprotected));
        Assert.True(unseeded.Count == 0, "Tenant A has no rows in: " + string.Join(", ", unseeded));
        Assert.True(leaks.Count == 0, "Tenant B can see tenant-A rows in: " + string.Join(", ", leaks));
    }
}
