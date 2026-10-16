using Npgsql;

namespace Erp.Integration.Tests;

internal sealed class ScratchDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;
    private readonly string _name;

    private ScratchDatabase(string serverConnectionString, string name, string ownerRole)
    {
        _serverConnectionString = serverConnectionString;
        _name = name;
        AdminConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString;
        OwnerConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = ownerRole,
            Password = ownerRole
        }.ConnectionString;
    }

    public string AdminConnectionString { get; }
    public string OwnerConnectionString { get; }

    public static async Task<ScratchDatabase> CreateAsync(string serverConnectionString, string ownerRole)
    {
        var scratch = new ScratchDatabase(serverConnectionString, "erp_scratch_" + Guid.NewGuid().ToString("N"), ownerRole);
        await scratch.ExecuteOnServerAsync($"create database {scratch._name}");
        return scratch;
    }

    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(AdminConnectionString));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(OwnerConnectionString));
        await ExecuteOnServerAsync($"drop database if exists {_name} with (force)");
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
