using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateDatabaseRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CREATE ROLE cannot run inside the migration transaction.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_owner') THEN
                        CREATE ROLE erp_owner NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') THEN
                        CREATE ROLE erp_app NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
                    END IF;
                END
                $$;
                """,
                suppressTransaction: true);

            migrationBuilder.Sql(
                """
                GRANT USAGE ON SCHEMA erp TO erp_app, erp_worker;
                REVOKE CREATE ON SCHEMA erp FROM PUBLIC, erp_app, erp_worker;

                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA erp TO erp_app, erp_worker;
                GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA erp TO erp_app, erp_worker;

                DO $history$
                BEGIN
                    IF to_regclass('erp."__EFMigrationsHistory"') IS NOT NULL THEN
                        EXECUTE 'REVOKE ALL ON TABLE erp."__EFMigrationsHistory" FROM erp_app, erp_worker';
                    END IF;
                END
                $history$;

                REVOKE ALL ON FUNCTION erp.claim_outbox(integer) FROM PUBLIC, erp_app;
                REVOKE ALL ON FUNCTION erp.replay_outbox(uuid) FROM PUBLIC, erp_app;
                GRANT EXECUTE ON FUNCTION erp.claim_outbox(integer), erp.replay_outbox(uuid) TO erp_worker;

                ALTER DEFAULT PRIVILEGES IN SCHEMA erp
                    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app, erp_worker;
                ALTER DEFAULT PRIVILEGES IN SCHEMA erp
                    GRANT USAGE, SELECT ON SEQUENCES TO erp_app, erp_worker;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER DEFAULT PRIVILEGES IN SCHEMA erp
                    REVOKE SELECT, INSERT, UPDATE, DELETE ON TABLES FROM erp_app, erp_worker;
                ALTER DEFAULT PRIVILEGES IN SCHEMA erp
                    REVOKE USAGE, SELECT ON SEQUENCES FROM erp_app, erp_worker;
                REVOKE ALL ON ALL TABLES IN SCHEMA erp FROM erp_app;
                REVOKE ALL ON ALL SEQUENCES IN SCHEMA erp FROM erp_app;
                REVOKE USAGE ON SCHEMA erp FROM erp_app;
                """);
        }
    }
}
