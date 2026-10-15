using Npgsql;

namespace Erp.Worker;

public static class OutboxReplay
{
    public static async Task<bool> ReplayAsync(string connectionString, Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select erp.replay_outbox(@id)", connection);
        command.Parameters.AddWithValue("id", messageId);
        var replayed = await command.ExecuteScalarAsync(cancellationToken);
        return replayed is int count ? count == 1 : replayed is long longCount && longCount == 1;
    }
}
