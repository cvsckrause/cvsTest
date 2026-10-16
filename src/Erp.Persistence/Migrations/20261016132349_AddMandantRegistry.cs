using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMandantRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "mandant_number_seq",
                schema: "erp",
                startValue: 1001L);

            migrationBuilder.CreateTable(
                name: "mandanten",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('erp.mandant_number_seq')"),
                    Firmenname = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ApprovalTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ApprovalExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mandanten", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "mandant_users",
                schema: "erp",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MandantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CognitoSub = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mandant_users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mandant_users_mandanten_MandantId",
                        column: x => x.MandantId,
                        principalSchema: "erp",
                        principalTable: "mandanten",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mandant_users_MandantId_UserName",
                schema: "erp",
                table: "mandant_users",
                columns: new[] { "MandantId", "UserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mandanten_ApprovalTokenHash",
                schema: "erp",
                table: "mandanten",
                column: "ApprovalTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mandanten_Number",
                schema: "erp",
                table: "mandanten",
                column: "Number",
                unique: true);

            // The API reads the registry before a tenant is known; the worker never needs it.
            migrationBuilder.Sql("""
                GRANT SELECT, INSERT, UPDATE ON TABLE erp."mandanten", erp."mandant_users" TO erp_app;
                GRANT USAGE, SELECT ON SEQUENCE erp."mandant_number_seq" TO erp_app;
                REVOKE ALL ON TABLE erp."mandanten", erp."mandant_users" FROM erp_worker;
                REVOKE ALL ON SEQUENCE erp."mandant_number_seq" FROM erp_worker;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mandant_users",
                schema: "erp");

            migrationBuilder.DropTable(
                name: "mandanten",
                schema: "erp");

            migrationBuilder.DropSequence(
                name: "mandant_number_seq",
                schema: "erp");
        }
    }
}
