using System.Text.RegularExpressions;
using Erp.BuildingBlocks;
using Erp.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Integration.Tests;

internal sealed class DatabaseProbe(string appConnectionString, string adminConnectionString)
{
    public static IReadOnlyList<TenantTable> TenantTables()
    {
        var options = new DbContextOptionsBuilder<ErpDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused")
            .Options;
        using var db = new ErpDbContext(options, new TenantContext(Guid.Empty, "model"));
        return db.Model.GetEntityTypes()
            .Where(entity => typeof(ITenantEntity).IsAssignableFrom(entity.ClrType))
            .Select(entity => new TenantTable(
                entity.GetSchema() ?? "erp",
                entity.GetTableName() ?? throw new InvalidOperationException($"{entity.ClrType.Name} has no table.")))
            .OrderBy(table => table.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<(string User, bool Superuser, bool BypassRls)> CurrentRoleAsync()
    {
        return await WithAppAsync(Guid.NewGuid(), async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select current_user, rolsuper, rolbypassrls
                from pg_roles
                where rolname = current_user
                """,
                connection);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException("current_user was not found in pg_roles.");
            }

            return (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
        });
    }

    public async Task<string> CreateTableAsAppAsync()
    {
        await using var connection = new NpgsqlConnection(appConnectionString);
        await connection.OpenAsync();
        try
        {
            await using var command = new NpgsqlCommand("create table erp.r17_ddl_probe (id integer)", connection);
            await command.ExecuteNonQueryAsync();
        }
        catch (PostgresException exception)
        {
            return exception.SqlState;
        }

        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand("drop table if exists erp.r17_ddl_probe", admin);
        await drop.ExecuteNonQueryAsync();
        return "ok";
    }

    public async Task<(bool Enabled, bool Forced, bool Policy)> IsolationAsync(TenantTable table)
    {
        await using var connection = new NpgsqlConnection(appConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select c.relrowsecurity,
                   c.relforcerowsecurity,
                   exists (
                       select 1
                       from pg_policy policy
                       where policy.polrelid = c.oid
                         and policy.polname = 'tenant_isolation')
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where n.nspname = @schema and c.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("schema", table.Schema);
        command.Parameters.AddWithValue("table", table.Name);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Table {table.Qualified} does not exist.");
        }

        return (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    public Task<int> CountVisibleOwnerRowsAsync(Guid actingTenant, TenantTable table, Guid ownerTenant) =>
        WithAppAsync(actingTenant, connection => CountAsync(
            connection,
            $"select count(*)::int from {table.Qualified} where \"TenantId\" = @owner",
            ownerTenant));

    public async Task<int> CountOwnerRowsAsync(TenantTable table, Guid ownerTenant)
    {
        await using var connection = new NpgsqlConnection(adminConnectionString);
        await connection.OpenAsync();
        return await CountAsync(
            connection,
            $"select count(*)::int from {table.Qualified} where \"TenantId\" = @owner",
            ownerTenant);
    }

    public Task<int> CountOrdersAsync(Guid tenantId, string orderNumber) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select count(*)::int
                from erp.sales_orders
                where "TenantId" = @tenant and "OrderNumber" = @order_number
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("order_number", orderNumber);
            return (int)(await command.ExecuteScalarAsync() ?? 0);
        });

    public Task<int> CountOutboxForOrderAsync(Guid tenantId, Guid salesOrderId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select count(*)::int
                from erp.outbox_messages
                where "TenantId" = @tenant and "Payload" like @pattern
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("pattern", "%" + salesOrderId + "%");
            return (int)(await command.ExecuteScalarAsync() ?? 0);
        });

    public Task<int> CountMovementsAsync(Guid tenantId, string reference) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select count(*)::int
                from erp.stock_movements
                where "TenantId" = @tenant and "Reference" = @reference
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("reference", reference);
            return (int)(await command.ExecuteScalarAsync() ?? 0);
        });

    public Task<int> CountOutboxForOrderEventAsync(Guid tenantId, Guid salesOrderId, string eventType) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select count(*)::int
                from erp.outbox_messages
                where "TenantId" = @tenant
                  and "EventType" = @event_type
                  and "Payload" like @pattern
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("event_type", eventType);
            command.Parameters.AddWithValue("pattern", "%" + salesOrderId + "%");
            return (int)(await command.ExecuteScalarAsync() ?? 0);
        });

    public Task<Guid> OutboxIdForOrderEventAsync(Guid tenantId, Guid salesOrderId, string eventType) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "Id"
                from erp.outbox_messages
                where "TenantId" = @tenant
                  and "EventType" = @event_type
                  and "Payload" like @pattern
                order by "OccurredAt" desc
                limit 1
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("event_type", eventType);
            command.Parameters.AddWithValue("pattern", "%" + salesOrderId + "%");
            var value = await command.ExecuteScalarAsync();
            return value is Guid id
                ? id
                : throw new InvalidOperationException($"No {eventType} outbox message for order {salesOrderId}.");
        });

    public Task<bool> OutboxProcessedAsync(Guid tenantId, Guid messageId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "ProcessedAt" is not null and "DeadLetteredAt" is null
                from erp.outbox_messages
                where "Id" = @id and "TenantId" = @tenant
                """,
                connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            var value = await command.ExecuteScalarAsync();
            return value is bool processed
                ? processed
                : throw new InvalidOperationException($"No outbox message {messageId}.");
        });

    public Task<int> CountDeliveriesAsync(Guid tenantId, string number) =>
        CountByNumberAsync(tenantId, "deliveries", number);

    public Task<int> CountInvoicesAsync(Guid tenantId, string number) =>
        CountByNumberAsync(tenantId, "invoices", number);

    public Task<int> CountDeliveriesForOrderAsync(Guid tenantId, Guid salesOrderId) =>
        CountForOrderAsync(tenantId, "deliveries", salesOrderId);

    public Task<int> CountInvoicesForOrderAsync(Guid tenantId, Guid salesOrderId) =>
        CountForOrderAsync(tenantId, "invoices", salesOrderId);

    public async Task<string> DeliveryNumberForOrderAsync(Guid tenantId, Guid salesOrderId)
    {
        var number = await NumberForOrderAsync(tenantId, "deliveries", salesOrderId);
        return number ?? throw new InvalidOperationException($"No delivery for order {salesOrderId}.");
    }

    public async Task<IReadOnlyList<long>> InvoiceSequenceForOrdersAsync(Guid tenantId, IReadOnlyCollection<Guid> salesOrderIds)
    {
        var numbers = await WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "Number"
                from erp.invoices
                where "TenantId" = @tenant and "SalesOrderId" = any(@orders)
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("orders", salesOrderIds.ToArray());
            var values = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) values.Add(reader.GetString(0));
            return values;
        });

        return numbers.Select(InvoiceValue).OrderBy(value => value).ToArray();
    }

    public Task<IReadOnlyList<long>> InvoiceSequenceAsync(Guid tenantId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "Number"
                from erp.invoices
                where "TenantId" = @tenant
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            var values = new List<long>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) values.Add(InvoiceValue(reader.GetString(0)));
            values.Sort();
            return (IReadOnlyList<long>)values;
        });

    public Task<Guid> OutboxIdForOrderAsync(Guid tenantId, Guid salesOrderId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "Id"
                from erp.outbox_messages
                where "TenantId" = @tenant and "Payload" like @pattern
                order by "OccurredAt" desc
                limit 1
                """,
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("pattern", "%" + salesOrderId + "%");
            var value = await command.ExecuteScalarAsync();
            return value is Guid id
                ? id
                : throw new InvalidOperationException($"No outbox message for order {salesOrderId}.");
        });

    public Task<string> PayloadAsync(Guid tenantId, Guid messageId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "Payload"
                from erp.outbox_messages
                where "Id" = @id and "TenantId" = @tenant
                """,
                connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            return (string)(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException($"No payload for outbox message {messageId}."));
        });

    public Task ReplacePayloadAsync(Guid tenantId, Guid messageId, string payload) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                update erp.outbox_messages
                set "Payload" = @payload
                where "Id" = @id and "TenantId" = @tenant
                """,
                connection);
            command.Parameters.AddWithValue("payload", payload);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            var updated = await command.ExecuteNonQueryAsync();
            if (updated != 1)
            {
                throw new InvalidOperationException($"Could not update payload for outbox message {messageId}.");
            }

            return 0;
        });

    public Task<(int Attempts, bool DeadLettered)> OutboxStateAsync(Guid tenantId, Guid messageId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                select "AttemptCount", "DeadLetteredAt" is not null
                from erp.outbox_messages
                where "Id" = @id and "TenantId" = @tenant
                """,
                connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            await using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                throw new InvalidOperationException($"No outbox message {messageId}.");
            }

            return (reader.GetInt32(0), reader.GetBoolean(1));
        });

    public async Task<(string User, bool Superuser, bool BypassRls)> RoleAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select current_user, rolsuper, rolbypassrls
            from pg_roles
            where rolname = current_user
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("current_user was not found in pg_roles.");
        }

        return (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    public Task<int> ClearProcessedAtAsync(Guid tenantId, Guid messageId) =>
        WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                """
                update erp.outbox_messages
                set "ProcessedAt" = null, "LeaseUntil" = null
                where "Id" = @id and "TenantId" = @tenant
                """,
                connection);
            command.Parameters.AddWithValue("id", messageId);
            command.Parameters.AddWithValue("tenant", tenantId);
            return await command.ExecuteNonQueryAsync();
        });

    public async Task<int> CountInboxAsync(Guid messageId)
    {
        await using var connection = new NpgsqlConnection(appConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            select count(*)::int
            from erp.inbox_messages
            where "MessageId" = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", messageId);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static long InvoiceValue(string number)
    {
        var match = Regex.Match(number, @"^R-(\d+)$");
        return match.Success
            ? long.Parse(match.Groups[1].Value)
            : throw new InvalidOperationException($"Invoice number '{number}' is not R-<digits>.");
    }

    private Task<int> CountByNumberAsync(Guid tenantId, string table, string number)
    {
        var qualified = new TenantTable("erp", table).Qualified;
        return WithAppAsync(tenantId, connection => CountAsync(
            connection,
            $"select count(*)::int from {qualified} where \"TenantId\" = @owner and \"Number\" = @number",
            tenantId,
            command => command.Parameters.AddWithValue("number", number)));
    }

    private Task<int> CountForOrderAsync(Guid tenantId, string table, Guid salesOrderId)
    {
        var qualified = new TenantTable("erp", table).Qualified;
        return WithAppAsync(tenantId, connection => CountAsync(
            connection,
            $"select count(*)::int from {qualified} where \"TenantId\" = @owner and \"SalesOrderId\" = @order",
            tenantId,
            command => command.Parameters.AddWithValue("order", salesOrderId)));
    }

    private Task<string?> NumberForOrderAsync(Guid tenantId, string table, Guid salesOrderId)
    {
        var qualified = new TenantTable("erp", table).Qualified;
        return WithAppAsync(tenantId, async connection =>
        {
            await using var command = new NpgsqlCommand(
                $"select \"Number\" from {qualified} where \"TenantId\" = @tenant and \"SalesOrderId\" = @order",
                connection);
            command.Parameters.AddWithValue("tenant", tenantId);
            command.Parameters.AddWithValue("order", salesOrderId);
            var value = await command.ExecuteScalarAsync();
            return value as string;
        });
    }

    private async Task<T> WithAppAsync<T>(Guid actingTenant, Func<NpgsqlConnection, Task<T>> action)
    {
        await using var connection = new NpgsqlConnection(appConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select set_config('app.tenant_id', @tenant_id, false)", connection);
        command.Parameters.AddWithValue("tenant_id", actingTenant.ToString());
        var applied = (string?)await command.ExecuteScalarAsync();
        if (!string.Equals(applied, actingTenant.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"app.tenant_id is '{applied}', expected '{actingTenant}'.");
        }

        return await action(connection);
    }

    private static async Task<int> CountAsync(
        NpgsqlConnection connection,
        string sql,
        Guid ownerTenant,
        Action<NpgsqlCommand>? bind = null)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("owner", ownerTenant);
        bind?.Invoke(command);
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }
}

internal readonly record struct TenantTable(string Schema, string Name)
{
    public string Qualified { get; } = Quote(Schema) + "." + Quote(Name);

    private static string Quote(string identifier)
    {
        if (!Regex.IsMatch(identifier, "^[a-z_][a-z0-9_]*$"))
        {
            throw new InvalidOperationException($"Unexpected SQL identifier '{identifier}'.");
        }

        return "\"" + identifier + "\"";
    }
}
