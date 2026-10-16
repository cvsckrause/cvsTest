using System.Data.Common;
using Erp.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Erp.Persistence;

public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        SetTenant(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default) =>
        await SetTenantAsync(connection, cancellationToken);

    private void SetTenant(DbConnection connection)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    private async Task SetTenantAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        if (tenantContext.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("A verified tenant is required before opening the database.");
        }

        var command = connection.CreateCommand();
        // Session-scoped. This runs before a transaction exists, so SET LOCAL would not stick.
        // Npgsql clears the GUC with DISCARD ALL when the connection returns to the pool. See ADR-002.
        command.CommandText = "select set_config('app.tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.Value = tenantContext.TenantId.ToString();
        command.Parameters.Add(parameter);
        return command;
    }
}
