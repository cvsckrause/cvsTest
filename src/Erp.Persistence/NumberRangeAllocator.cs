using System.Data;
using System.Data.Common;
using Erp.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Erp.Persistence;

public static class NumberRangeAllocator
{
    public static async Task<string> AllocateAsync(
        ErpDbContext db,
        Guid tenantId,
        NumberDocumentType documentType,
        DateTimeOffset allocatedAt,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Number allocation requires the caller's transaction.");
        }

        var defaults = NumberRangePolicy.Defaults(documentType);
        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        var transaction = db.Database.CurrentTransaction.GetDbTransaction();
        var documentTypeName = documentType.ToString();
        var policy = await ReadPolicyAsync(connection, transaction, tenantId, documentTypeName, cancellationToken);
        var prefix = policy?.Prefix ?? defaults.Prefix;
        var resetYearly = policy?.ResetYearly ?? defaults.ResetYearly;
        var year = NumberRangePolicy.YearBucket(resetYearly, allocatedAt);

        await ExecuteAsync(
            connection,
            transaction,
            """
            insert into erp.number_ranges
                ("Id", "TenantId", "DocumentType", "Prefix", "NextValue", "Year", "ResetYearly")
            values (@id, @tenant, @type, @prefix, 1, @year, @reset)
            on conflict ("TenantId", "DocumentType", "Year") do nothing
            """,
            cancellationToken,
            ("id", (object)Guid.NewGuid()),
            ("tenant", (object)tenantId),
            ("type", documentTypeName),
            ("prefix", prefix),
            ("year", (object)year),
            ("reset", (object)resetYearly));

        var allocated = await ReadLockedAsync(connection, transaction, tenantId, documentTypeName, year, cancellationToken);
        await ExecuteAsync(
            connection,
            transaction,
            """
            update erp.number_ranges
            set "NextValue" = "NextValue" + 1
            where "TenantId" = @tenant and "DocumentType" = @type and "Year" = @year
            """,
            cancellationToken,
            ("tenant", (object)tenantId),
            ("type", documentTypeName),
            ("year", (object)year));

        return NumberRangePolicy.Format(allocated.Prefix, allocated.NextValue);
    }

    private static async Task<RangePolicy?> ReadPolicyAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid tenantId,
        string documentType,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            select "Prefix", "ResetYearly"
            from erp.number_ranges
            where "TenantId" = @tenant and "DocumentType" = @type
            order by "Year"
            limit 1
            """,
            ("tenant", (object)tenantId),
            ("type", documentType));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new RangePolicy(reader.GetString(0), reader.GetBoolean(1));
    }

    private static async Task<AllocatedValue> ReadLockedAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid tenantId,
        string documentType,
        int year,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            transaction,
            """
            select "Prefix", "NextValue"
            from erp.number_ranges
            where "TenantId" = @tenant and "DocumentType" = @type and "Year" = @year
            for update
            """,
            ("tenant", (object)tenantId),
            ("type", documentType),
            ("year", (object)year));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Number range {documentType} for year {year} was not created.");
        }

        return new AllocatedValue(reader.GetString(0), reader.GetInt64(1));
    }

    private static async Task ExecuteAsync(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand CreateCommand(
        DbConnection connection,
        DbTransaction transaction,
        string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private readonly record struct RangePolicy(string Prefix, bool ResetYearly);
    private readonly record struct AllocatedValue(string Prefix, long NextValue);
}
