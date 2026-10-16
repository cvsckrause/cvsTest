using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreezeOrderLineContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_order_lines_TenantId_SalesOrderId",
                schema: "erp",
                table: "sales_order_lines");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "erp",
                table: "sales_order_lines",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Position",
                schema: "erp",
                table: "sales_order_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TaxCode",
                schema: "erp",
                table: "sales_order_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard19");

            // The migrator runs with one tenant's app.tenant_id, and forced RLS binds the owner on the
            // joined table too, so both tables lift FORCE for the backfill.
            // Existing lines never recorded their capture order. Lines are insert-only, so physical
            // order (ctid) is the best remaining hint; it is read before the description update
            // rewrites the rows.
            migrationBuilder.Sql("""
                ALTER TABLE erp."sales_order_lines" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE erp."articles" NO FORCE ROW LEVEL SECURITY;
                UPDATE erp."sales_order_lines" AS line
                SET "Position" = numbered.position
                FROM (
                    SELECT ctid AS row_ref,
                           row_number() OVER (PARTITION BY "SalesOrderId" ORDER BY ctid) AS position
                    FROM erp."sales_order_lines"
                ) AS numbered
                WHERE line.ctid = numbered.row_ref;
                UPDATE erp."sales_order_lines" AS line
                SET "Description" = article."Name"
                FROM erp."articles" AS article
                WHERE article."Id" = line."ArticleId" AND article."TenantId" = line."TenantId";
                ALTER TABLE erp."articles" FORCE ROW LEVEL SECURITY;
                ALTER TABLE erp."sales_order_lines" FORCE ROW LEVEL SECURITY;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_sales_order_lines_TenantId_SalesOrderId_Position",
                schema: "erp",
                table: "sales_order_lines",
                columns: new[] { "TenantId", "SalesOrderId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_sales_order_lines_TenantId_SalesOrderId_Position",
                schema: "erp",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "erp",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "Position",
                schema: "erp",
                table: "sales_order_lines");

            migrationBuilder.DropColumn(
                name: "TaxCode",
                schema: "erp",
                table: "sales_order_lines");

            migrationBuilder.CreateIndex(
                name: "IX_sales_order_lines_TenantId_SalesOrderId",
                schema: "erp",
                table: "sales_order_lines",
                columns: new[] { "TenantId", "SalesOrderId" });
        }
    }
}
