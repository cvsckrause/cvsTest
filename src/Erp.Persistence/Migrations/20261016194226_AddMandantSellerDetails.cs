using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMandantSellerDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SellerAdditionalLine",
                schema: "erp",
                table: "mandanten",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCity",
                schema: "erp",
                table: "mandanten",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerCountryCode",
                schema: "erp",
                table: "mandanten",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPostalCode",
                schema: "erp",
                table: "mandanten",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerStreet",
                schema: "erp",
                table: "mandanten",
                type: "character varying(240)",
                maxLength: 240,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Steuernummer",
                schema: "erp",
                table: "mandanten",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UstIdNr",
                schema: "erp",
                table: "mandanten",
                type: "character varying(14)",
                maxLength: 14,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SellerAdditionalLine",
                schema: "erp",
                table: "mandanten");

            migrationBuilder.DropColumn(
                name: "SellerCity",
                schema: "erp",
                table: "mandanten");

            migrationBuilder.DropColumn(
                name: "SellerCountryCode",
                schema: "erp",
                table: "mandanten");

            migrationBuilder.DropColumn(
                name: "SellerPostalCode",
                schema: "erp",
                table: "mandanten");

            migrationBuilder.DropColumn(
                name: "SellerStreet",
                schema: "erp",
                table: "mandanten");

            migrationBuilder.DropColumn(
                name: "Steuernummer",
                schema: "erp",
                table: "mandanten");

            migrationBuilder.DropColumn(
                name: "UstIdNr",
                schema: "erp",
                table: "mandanten");
        }
    }
}
