using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnableAddressRowLevelSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "addresses", "customer_addresses" })
            {
                migrationBuilder.Sql($"""
                    ALTER TABLE erp."{table}" ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE erp."{table}" FORCE ROW LEVEL SECURITY;
                    CREATE POLICY tenant_isolation ON erp."{table}"
                    USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
                    WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "addresses", "customer_addresses" })
            {
                migrationBuilder.Sql($"""
                    DROP POLICY IF EXISTS tenant_isolation ON erp."{table}";
                    ALTER TABLE erp."{table}" NO FORCE ROW LEVEL SECURITY;
                    ALTER TABLE erp."{table}" DISABLE ROW LEVEL SECURITY;
                    """);
            }
        }
    }
}
