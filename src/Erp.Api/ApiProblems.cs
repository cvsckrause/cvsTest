using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Erp.Api;

internal readonly record struct ApiProblemDecision(int? Status, string? Title, string? Detail, bool Replay)
{
    public const string RetryDetail = "The record was changed by another request. Retry the operation.";

    public static ApiProblemDecision Validation(string detail) =>
        new(StatusCodes.Status400BadRequest, "The request is invalid.", detail, false);

    public static ApiProblemDecision NotFound(string detail) =>
        new(StatusCodes.Status404NotFound, "The resource was not found.", detail, false);

    public static ApiProblemDecision Conflict(string detail) =>
        new(StatusCodes.Status409Conflict, "The request conflicts with the current state.", detail, false);

    public static ApiProblemDecision IdempotencyReplay() => new(null, null, null, true);

    public static ApiProblemDecision Unhandled() => new(null, null, null, false);

    public static ApiProblemDecision From(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
            return Conflict(RetryDetail);

        if (exception is DbUpdateException && FindPostgres(exception) is { SqlState: PostgresErrorCodes.UniqueViolation } postgres)
        {
            return IsIdempotencyConflict(postgres)
                ? IdempotencyReplay()
                : Conflict(DuplicateDetail(postgres));
        }

        return exception switch
        {
            ArgumentException => Validation(exception.Message),
            KeyNotFoundException => NotFound(exception.Message),
            InvalidOperationException => Conflict(exception.Message),
            _ => Unhandled()
        };
    }

    private static PostgresException? FindPostgres(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres) return postgres;
        }

        return null;
    }

    private static bool IsIdempotencyConflict(PostgresException postgres) =>
        string.Equals(postgres.TableName, "idempotency_records", StringComparison.OrdinalIgnoreCase)
        || (postgres.ConstraintName?.Contains("idempotency", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string DuplicateDetail(PostgresException postgres)
    {
        var table = postgres.TableName ?? string.Empty;
        var constraint = postgres.ConstraintName ?? string.Empty;
        if (table.Equals("articles", StringComparison.OrdinalIgnoreCase) || constraint.Contains("Sku", StringComparison.Ordinal))
            return "An article with this SKU already exists.";
        if (table.Equals("customers", StringComparison.OrdinalIgnoreCase))
            return "A customer with this number already exists.";
        if (table.Equals("sales_orders", StringComparison.OrdinalIgnoreCase))
            return "An order with this number already exists.";

        return "The request conflicts with an existing record.";
    }
}
