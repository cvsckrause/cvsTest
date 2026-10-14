using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ArticleAddressManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShipToAdditionalLine",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ShipToAddressId",
                schema: "erp",
                table: "sales_orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountryCode",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToName",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToPostalCode",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToAdditionalLine",
                schema: "erp",
                table: "deliveries",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipToCity",
                schema: "erp",
                table: "deliveries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToCountryCode",
                schema: "erp",
                table: "deliveries",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToName",
                schema: "erp",
                table: "deliveries",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToPostalCode",
                schema: "erp",
                table: "deliveries",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipToStreet",
                schema: "erp",
                table: "deliveries",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "addresses",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Street = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    AdditionalLine = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customer_addresses",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_addresses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_addresses_TenantId_Name",
                schema: "erp",
                table: "addresses",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_addresses_TenantId_CustomerId_AddressId_Role",
                schema: "erp",
                table: "customer_addresses",
                columns: new[] { "TenantId", "CustomerId", "AddressId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_addresses_TenantId_CustomerId_Role",
                schema: "erp",
                table: "customer_addresses",
                columns: new[] { "TenantId", "CustomerId", "Role" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "customer_addresses",
                schema: "erp");

            migrationBuilder.DropColumn(
                name: "ShipToAdditionalLine",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToAddressId",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToCity",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToCountryCode",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToName",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToPostalCode",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToStreet",
                schema: "erp",
                table: "sales_orders");

            migrationBuilder.DropColumn(
                name: "ShipToAdditionalLine",
                schema: "erp",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ShipToCity",
                schema: "erp",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ShipToCountryCode",
                schema: "erp",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ShipToName",
                schema: "erp",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ShipToPostalCode",
                schema: "erp",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "ShipToStreet",
                schema: "erp",
                table: "deliveries");
        }
    }
}
