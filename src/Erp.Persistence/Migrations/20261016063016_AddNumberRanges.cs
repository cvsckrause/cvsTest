using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNumberRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "number_ranges",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    ResetYearly = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_number_ranges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_number_ranges_TenantId_DocumentType_Year",
                schema: "erp",
                table: "number_ranges",
                columns: new[] { "TenantId", "DocumentType", "Year" },
                unique: true);

            migrationBuilder.Sql("""
                ALTER TABLE erp."number_ranges" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE erp."number_ranges" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON erp."number_ranges"
                USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
                WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE erp."number_ranges" TO erp_worker;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "number_ranges",
                schema: "erp");
        }
    }
}
