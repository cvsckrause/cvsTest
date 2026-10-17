using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurgeIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // erp_worker is NOBYPASSRLS, so a tenant-scoped delete cannot see another tenant's keys.
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION erp.purge_idempotency(retention_days integer)
                RETURNS integer
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = erp, pg_temp
                AS $$
                DECLARE
                    removed integer;
                BEGIN
                    IF retention_days IS NULL OR retention_days <= 0 THEN
                        RETURN 0;
                    END IF;

                    DELETE FROM erp.idempotency_records
                    WHERE "CreatedAt" < now() - make_interval(days => retention_days);
                    GET DIAGNOSTICS removed = ROW_COUNT;
                    RETURN removed;
                END;
                $$;

                GRANT CREATE ON SCHEMA erp TO erp_outbox_definer;
                ALTER FUNCTION erp.purge_idempotency(integer) OWNER TO erp_outbox_definer;
                REVOKE CREATE ON SCHEMA erp FROM erp_outbox_definer;
                REVOKE ALL ON FUNCTION erp.purge_idempotency(integer) FROM PUBLIC;
                REVOKE ALL ON FUNCTION erp.purge_idempotency(integer) FROM erp_app;
                GRANT EXECUTE ON FUNCTION erp.purge_idempotency(integer) TO erp_worker;
                GRANT SELECT, DELETE ON erp.idempotency_records TO erp_outbox_definer;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP FUNCTION IF EXISTS erp.purge_idempotency(integer);
                REVOKE SELECT, DELETE ON erp.idempotency_records FROM erp_outbox_definer;
                """);
        }
    }
}
