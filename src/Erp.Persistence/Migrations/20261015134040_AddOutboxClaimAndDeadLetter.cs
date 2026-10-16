using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxClaimAndDeadLetter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_outbox_definer') THEN
                        CREATE ROLE erp_outbox_definer NOLOGIN BYPASSRLS;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_worker') THEN
                        CREATE ROLE erp_worker NOLOGIN NOSUPERUSER NOBYPASSRLS;
                    END IF;
                END
                $$;
                """,
                suppressTransaction: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeadLetteredAt",
                schema: "erp",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseUntil",
                schema: "erp",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextAttemptAt",
                schema: "erp",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION erp.claim_outbox(batch_size integer)
                RETURNS TABLE (id uuid, tenant_id uuid)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = erp, pg_temp
                AS $$
                    WITH claimed AS (
                        SELECT message."Id"
                        FROM erp.outbox_messages AS message
                        WHERE message."ProcessedAt" IS NULL
                          AND message."DeadLetteredAt" IS NULL
                          AND (message."NextAttemptAt" IS NULL OR message."NextAttemptAt" <= now())
                          AND (message."LeaseUntil" IS NULL OR message."LeaseUntil" <= now())
                        ORDER BY message."OccurredAt"
                        LIMIT batch_size
                        FOR UPDATE SKIP LOCKED
                    )
                    UPDATE erp.outbox_messages AS message
                    SET "LeaseUntil" = now() + interval '30 seconds'
                    FROM claimed
                    WHERE message."Id" = claimed."Id"
                    RETURNING message."Id", message."TenantId";
                $$;

                CREATE OR REPLACE FUNCTION erp.replay_outbox(message_id uuid)
                RETURNS integer
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = erp, pg_temp
                AS $$
                    WITH replayed AS (
                        UPDATE erp.outbox_messages
                        SET "DeadLetteredAt" = NULL,
                            "ProcessedAt" = NULL,
                            "AttemptCount" = 0,
                            "LastError" = NULL,
                            "NextAttemptAt" = NULL,
                            "LeaseUntil" = NULL
                        WHERE "Id" = message_id
                          AND "DeadLetteredAt" IS NOT NULL
                        RETURNING 1
                    )
                    SELECT count(*)::integer FROM replayed;
                $$;

                GRANT CREATE ON SCHEMA erp TO erp_outbox_definer;
                ALTER FUNCTION erp.claim_outbox(integer) OWNER TO erp_outbox_definer;
                ALTER FUNCTION erp.replay_outbox(uuid) OWNER TO erp_outbox_definer;
                REVOKE CREATE ON SCHEMA erp FROM erp_outbox_definer;
                REVOKE ALL ON FUNCTION erp.claim_outbox(integer) FROM PUBLIC;
                REVOKE ALL ON FUNCTION erp.replay_outbox(uuid) FROM PUBLIC;
                GRANT USAGE ON SCHEMA erp TO erp_outbox_definer, erp_worker;
                GRANT SELECT, UPDATE ON erp.outbox_messages TO erp_outbox_definer;
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA erp TO erp_worker;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA erp TO erp_worker;
                GRANT EXECUTE ON FUNCTION erp.claim_outbox(integer), erp.replay_outbox(uuid) TO erp_worker;
                GRANT erp_worker TO CURRENT_USER;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS erp.replay_outbox(uuid);
                DROP FUNCTION IF EXISTS erp.claim_outbox(integer);
                """);

            migrationBuilder.DropColumn(
                name: "DeadLetteredAt",
                schema: "erp",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "LeaseUntil",
                schema: "erp",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                schema: "erp",
                table: "outbox_messages");
        }
    }
}
