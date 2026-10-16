using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Erp.Integration.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationHistoryTests(PostgresFixture fixture)
{
    private const string OwnerRole = "erp_history_owner";
    private static readonly Guid MigratorTenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task A_non_superuser_owner_migrates_a_fresh_database_and_keeps_the_history_in_erp()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(fixture.AdminConnectionString, OwnerRole);
        await GrantMigratorPrivilegesAsync(scratch);

        await MigrateAsOwnerAsync(scratch);
        await MigrateAsOwnerAsync(scratch);

        Assert.Equal(
            ("erp.\"__EFMigrationsHistory\"", OwnerRole, (long)MigrationCount()),
            await HistoryAsync(scratch));
    }

    // Mirrors what Erp.Migrator grants erp_owner before it migrates: no superuser, no BYPASSRLS,
    // and no CREATE on public.
    private static async Task GrantMigratorPrivilegesAsync(ScratchDatabase scratch)
    {
        var database = new NpgsqlConnectionStringBuilder(scratch.AdminConnectionString).Database;
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            do $$
            begin
                create role {OwnerRole} login password '{OwnerRole}' nosuperuser nobypassrls nocreaterole;
            exception
                when duplicate_object then null;
            end
            $$;
            revoke create on schema public from public;
            grant connect, create on database {database} to {OwnerRole};
            grant erp_outbox_definer to {OwnerRole};
            grant erp_worker to {OwnerRole} with admin option;
            """,
            connection);
        await command.ExecuteNonQueryAsync();
    }

    private static int MigrationCount()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused")
            .Options;
        using var db = new ErpDbContext(options, new TenantContext(Guid.NewGuid(), "model"));
        return db.GetService<IMigrationsAssembly>().Migrations.Count;
    }

    private static async Task MigrateAsOwnerAsync(ScratchDatabase scratch)
    {
        var tenant = new TenantContext(MigratorTenant, "erp-migrator");
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql(scratch.OwnerConnectionString)
            .AddInterceptors(new TenantConnectionInterceptor(tenant))
            .Options;
        await using var db = new ErpDbContext(options, tenant);
        await db.GetService<IMigrator>().MigrateAsync();
    }

    private static async Task<(string Table, string Owner, long Rows)> HistoryAsync(ScratchDatabase scratch)
    {
        await using var connection = new NpgsqlConnection(scratch.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select c.oid::regclass::text, pg_get_userbyid(c.relowner), (select count(*) from erp."__EFMigrationsHistory")
            from pg_class c
            where c.relname = '__EFMigrationsHistory'
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var history = (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
        Assert.False(await reader.ReadAsync());
        return history;
    }
}
