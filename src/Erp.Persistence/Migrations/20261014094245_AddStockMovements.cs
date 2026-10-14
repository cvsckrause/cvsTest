using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_movements",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    WarehouseCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MovementType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_TenantId_OccurredAt",
                schema: "erp",
                table: "stock_movements",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.Sql("""
                ALTER TABLE erp."stock_movements" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE erp."stock_movements" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON erp."stock_movements"
                USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
                WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP POLICY IF EXISTS tenant_isolation ON erp."stock_movements";
                """);

            migrationBuilder.DropTable(
                name: "stock_movements",
                schema: "erp");
        }
    }
}
