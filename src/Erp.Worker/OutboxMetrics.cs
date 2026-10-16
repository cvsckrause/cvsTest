using Erp.Observability;
using Npgsql;

namespace Erp.Worker;

public static class OutboxMetrics
{
    public static async Task<OutboxSnapshot> ReadAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            select backlog, oldest_pending_age_seconds, dead_letter_count
            from erp.outbox_metrics()
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new OutboxSnapshot(0, 0, 0);
        }

        return new OutboxSnapshot(reader.GetInt32(0), reader.GetDouble(1), reader.GetInt32(2));
    }
}
