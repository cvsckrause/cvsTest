using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrefixInvoiceNumbers : Migration
    {
        // Forced RLS also binds the table owner, and the migrator runs with one tenant's
        // app.tenant_id, so the update must lift FORCE to reach every tenant's range.
        // DDL is transactional: a failure leaves FORCE in place.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE erp."number_ranges" NO FORCE ROW LEVEL SECURITY;
                UPDATE erp."number_ranges"
                SET "Prefix" = 'R-'
                WHERE "DocumentType" = 'Invoice' AND "Prefix" = '';
                ALTER TABLE erp."number_ranges" FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE erp."number_ranges" NO FORCE ROW LEVEL SECURITY;
                UPDATE erp."number_ranges"
                SET "Prefix" = ''
                WHERE "DocumentType" = 'Invoice' AND "Prefix" = 'R-';
                ALTER TABLE erp."number_ranges" FORCE ROW LEVEL SECURITY;
                """);
        }
    }
}
