using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                schema: "erp",
                table: "idempotency_records",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Fingerprint",
                schema: "erp",
                table: "idempotency_records");
        }
    }
}
