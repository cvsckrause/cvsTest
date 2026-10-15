using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesOrderWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WarehouseCode",
                schema: "erp",
                table: "sales_orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "MAIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WarehouseCode",
                schema: "erp",
                table: "sales_orders");
        }
    }
}
