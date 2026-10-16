using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreezeOrderBillTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillToAdditionalLine",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BillToAddressId",
                schema: "erp",
                table: "sales_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillToCity",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillToCountryCode",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillToName",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillToPostalCode",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BillToStreet",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BuyerNumber",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Existing orders bill the customer at the ship-to address, as an order without a Billing link
            // does. The migrator runs with one tenant's app.tenant_id, and forced RLS binds the owner on the
            // joined table too, so both tables lift FORCE for the backfill.
            migrationBuilder.Sql("""
                ALTER TABLE erp."sales_orders" NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE erp."customers" NO FORCE ROW LEVEL SECURITY;
                UPDATE erp."sales_orders" AS sales_order
                SET "BuyerName" = COALESCE(buyer.customer_name, sales_order."ShipToName"),
                    "BuyerNumber" = COALESCE(buyer.customer_number, ''),
                    "BillToName" = sales_order."ShipToName",
                    "BillToStreet" = sales_order."ShipToStreet",
                    "BillToPostalCode" = sales_order."ShipToPostalCode",
                    "BillToCity" = sales_order."ShipToCity",
                    "BillToCountryCode" = sales_order."ShipToCountryCode",
                    "BillToAdditionalLine" = sales_order."ShipToAdditionalLine"
                FROM (
                    SELECT existing."Id" AS order_id,
                           customer."Name" AS customer_name,
                           customer."Number" AS customer_number
                    FROM erp."sales_orders" AS existing
                    LEFT JOIN erp."customers" AS customer
                        ON customer."Id" = existing."CustomerId" AND customer."TenantId" = existing."TenantId"
                ) AS buyer
                WHERE sales_order."Id" = buyer.order_id;
                ALTER TABLE erp."customers" FORCE ROW LEVEL SECURITY;
                ALTER TABLE erp."sales_orders" FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillToAdditionalLine",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BillToAddressId",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BillToCity",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BillToCountryCode",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BillToName",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BillToPostalCode",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BillToStreet",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "BuyerNumber",
                schema: "erp",
                table: "sales_orders");
        }
    }
}
