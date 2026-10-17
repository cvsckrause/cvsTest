using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerAdditionalLine",
                schema: "erp",
                table: "invoices",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCity",
                schema: "erp",
                table: "invoices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCountryCode",
                schema: "erp",
                table: "invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                schema: "erp",
                table: "invoices",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerNumber",
                schema: "erp",
                table: "invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPostalCode",
                schema: "erp",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerStreet",
                schema: "erp",
                table: "invoices",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContentComplete",
                schema: "erp",
                table: "invoices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeliveryDate",
                schema: "erp",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GrossAmount",
                schema: "erp",
                table: "invoices",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "InvoiceDate",
                schema: "erp",
                table: "invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerAdditionalLine",
                schema: "erp",
                table: "invoices",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCity",
                schema: "erp",
                table: "invoices",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCountryCode",
                schema: "erp",
                table: "invoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerName",
                schema: "erp",
                table: "invoices",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPostalCode",
                schema: "erp",
                table: "invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerSteuernummer",
                schema: "erp",
                table: "invoices",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerStreet",
                schema: "erp",
                table: "invoices",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerUstIdNr",
                schema: "erp",
                table: "invoices",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxAmount",
                schema: "erp",
                table: "invoices",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Sku = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Category = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    TaxCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_lines_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "erp",
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_tax_totals",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Category = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    NetAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_tax_totals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_tax_totals_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "erp",
                        principalTable: "invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_InvoiceId",
                schema: "erp",
                table: "invoice_lines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_lines_TenantId_InvoiceId",
                schema: "erp",
                table: "invoice_lines",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_tax_totals_InvoiceId",
                schema: "erp",
                table: "invoice_tax_totals",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_tax_totals_TenantId_InvoiceId",
                schema: "erp",
                table: "invoice_tax_totals",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.Sql("""
                ALTER TABLE erp."invoice_lines" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE erp."invoice_lines" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON erp."invoice_lines"
                USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
                WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);

                ALTER TABLE erp."invoice_tax_totals" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE erp."invoice_tax_totals" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON erp."invoice_tax_totals"
                USING ("TenantId" = current_setting('app.tenant_id', true)::uuid)
                WITH CHECK ("TenantId" = current_setting('app.tenant_id', true)::uuid);

                REVOKE ALL ON TABLE erp."invoices", erp."invoice_lines", erp."invoice_tax_totals" FROM erp_app, erp_worker;
                GRANT SELECT ON TABLE erp."invoices", erp."invoice_lines", erp."invoice_tax_totals" TO erp_app;
                GRANT SELECT, INSERT ON TABLE erp."invoices", erp."invoice_lines", erp."invoice_tax_totals" TO erp_worker;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_lines",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "invoice_tax_totals",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "BuyerAdditionalLine",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerCity",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerCountryCode",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerNumber",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerPostalCode",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "BuyerStreet",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "ContentComplete",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "DeliveryDate",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "GrossAmount",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "InvoiceDate",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerAdditionalLine",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerCity",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerCountryCode",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerName",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerPostalCode",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerSteuernummer",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerStreet",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "SellerUstIdNr",
                schema: "erp",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "TaxAmount",
                schema: "erp",
                table: "invoices");

            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLE erp."invoices" TO erp_app, erp_worker;
                """);
        }
    }
}
