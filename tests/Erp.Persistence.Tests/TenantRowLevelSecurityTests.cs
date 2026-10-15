using System.Text.RegularExpressions;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Erp.Persistence.Tests;

public sealed class TenantRowLevelSecurityTests
{
    [Fact]
    public void Every_tenant_table_has_a_forced_isolation_policy()
    {
        using var db = CreateContext();
        var sql = db.GetService<IMigrator>().GenerateScript();
        var missing = TenantTables(db)
            .Where(table => !HasForcedTenantIsolation(sql, table))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Missing ENABLE, FORCE, and tenant_isolation for: " + string.Join(", ", missing));
    }

    private static ErpDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=erp_test")
            .Options;
        return new ErpDbContext(options, new TenantContext(Guid.Empty, "rls-guard"));
    }

    private static IReadOnlyList<string> TenantTables(ErpDbContext db)
    {
        return db.Model.GetEntityTypes()
            .Where(entity => typeof(ITenantEntity).IsAssignableFrom(entity.ClrType))
            .Select(entity => $"{entity.GetSchema() ?? "erp"}.{entity.GetTableName()}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasForcedTenantIsolation(string sql, string schemaAndTable)
    {
        var separator = schemaAndTable.IndexOf('.');
        var schema = schemaAndTable[..separator];
        var table = schemaAndTable[(separator + 1)..];
        var normalized = Regex.Replace(sql, @"\s+", " ");
        var qualified = $"{schema}.\"{table}\"";

        return normalized.Contains($"ALTER TABLE {qualified} ENABLE ROW LEVEL SECURITY", StringComparison.Ordinal)
            && normalized.Contains($"ALTER TABLE {qualified} FORCE ROW LEVEL SECURITY", StringComparison.Ordinal)
            && normalized.Contains(
                $"CREATE POLICY tenant_isolation ON {qualified} USING (\"TenantId\" = current_setting('app.tenant_id', true)::uuid) WITH CHECK (\"TenantId\" = current_setting('app.tenant_id', true)::uuid)",
                StringComparison.Ordinal);
    }
}
