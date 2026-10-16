using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxRequestContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                schema: "erp",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                schema: "erp",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // erp_worker has no BYPASSRLS, so a tenant-scoped read would hide every other
            // tenant. The definer role already bypasses RLS for claim and replay.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION erp.outbox_metrics()
                RETURNS TABLE (
                    backlog integer,
                    oldest_pending_age_seconds double precision,
                    dead_letter_count integer
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = erp, pg_temp
                AS $$
                    SELECT
                        count(*) FILTER (
                            WHERE "ProcessedAt" IS NULL AND "DeadLetteredAt" IS NULL
                        )::integer,
                        coalesce(extract(epoch FROM (
                            now() - min("OccurredAt") FILTER (
                                WHERE "ProcessedAt" IS NULL AND "DeadLetteredAt" IS NULL
                            )
                        )), 0)::double precision,
                        count(*) FILTER (
                            WHERE "ProcessedAt" IS NULL AND "DeadLetteredAt" IS NOT NULL
                        )::integer
                    FROM erp.outbox_messages;
                $$;

                GRANT CREATE ON SCHEMA erp TO erp_outbox_definer;
                ALTER FUNCTION erp.outbox_metrics() OWNER TO erp_outbox_definer;
                REVOKE CREATE ON SCHEMA erp FROM erp_outbox_definer;
                REVOKE ALL ON FUNCTION erp.outbox_metrics() FROM PUBLIC;
                GRANT EXECUTE ON FUNCTION erp.outbox_metrics() TO erp_worker;
                GRANT SELECT ON erp.outbox_messages TO erp_outbox_definer;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS erp.outbox_metrics();");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                schema: "erp",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "UserId",
                schema: "erp",
                table: "outbox_messages");
        }
    }
}
