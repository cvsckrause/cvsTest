using Erp.BuildingBlocks;

namespace Erp.Platform.Domain;

public sealed class AuditEntry : ITenantEntity
{
    private AuditEntry()
    {
    }

    private AuditEntry(
        Guid tenantId,
        string userId,
        string action,
        string entityType,
        Guid entityId,
        string? data,
        string correlationId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        UserId = userId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Data = data;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string? Data { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static AuditEntry Create(
        Guid tenantId,
        string userId,
        string action,
        string entityType,
        Guid entityId,
        string? data,
        string correlationId) =>
        new(tenantId, userId, action, entityType, entityId, data, correlationId);
}

public sealed class OutboxMessage : ITenantEntity
{
    private OutboxMessage()
    {
    }

    private OutboxMessage(Guid tenantId, string eventType, string payload, Guid eventId, DateTimeOffset occurredAt)
    {
        Id = eventId;
        TenantId = tenantId;
        EventType = eventType;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset? ProcessedAt { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? LeaseUntil { get; private set; }
    public DateTimeOffset? DeadLetteredAt { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage From(
        Guid tenantId,
        string eventType,
        string payload,
        Guid eventId,
        DateTimeOffset occurredAt) =>
        new(tenantId, eventType, payload, eventId, occurredAt);

    public void MarkProcessed()
    {
        ProcessedAt = DateTimeOffset.UtcNow;
        LeaseUntil = null;
    }

    public void MarkFailed(string error, DateTimeOffset nextAttemptAt, int maxAttempts)
    {
        AttemptCount++;
        LastError = error;
        LeaseUntil = null;
        if (AttemptCount >= maxAttempts)
        {
            DeadLetteredAt = DateTimeOffset.UtcNow;
            NextAttemptAt = null;
            return;
        }

        NextAttemptAt = nextAttemptAt;
    }
}

public sealed class IdempotencyRecord : ITenantEntity
{
    private IdempotencyRecord()
    {
    }

    private IdempotencyRecord(Guid tenantId, string key, string operation, Guid resourceId)
    {
        Id = Guid.NewGuid();
        TenantId = tenantId;
        Key = key;
        Operation = operation;
        ResourceId = resourceId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Operation { get; private set; } = string.Empty;
    public Guid ResourceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Create(Guid tenantId, string key, string operation, Guid resourceId) =>
        new(tenantId, key, operation, resourceId);
}

public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    public InboxMessage(Guid messageId)
    {
        MessageId = messageId;
        ProcessedAt = DateTimeOffset.UtcNow;
    }

    public Guid MessageId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }
}
