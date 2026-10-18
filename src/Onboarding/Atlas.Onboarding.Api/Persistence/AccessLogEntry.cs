using Atlas.BuildingBlocks.Security;

namespace Atlas.Onboarding.Api.Persistence;

public enum AccessAction
{
    Create,
    Read,
    Amend,
    Erase,
}

/// <summary>
/// One access to customer personal data or documents (GC-2026-0814 §2). Append-only: there is no code path that
/// updates or deletes an entry. Retained as long as the record it describes.
/// </summary>
public sealed class AccessLogEntry
{
    private AccessLogEntry()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public ActorType ActorType { get; private set; }

    public string ActorId { get; private set; } = string.Empty;

    public AccessAction Action { get; private set; }

    public string RecordType { get; private set; } = string.Empty;

    public string RecordId { get; private set; } = string.Empty;

    public string Purpose { get; private set; } = string.Empty;

    public string? CorrelationId { get; private set; }

    public DateTimeOffset RetainUntil { get; private set; }

    public static AccessLogEntry Create(
        Actor actor,
        AccessAction action,
        string recordType,
        string recordId,
        string purpose,
        string? correlationId,
        DateTimeOffset now,
        DateTimeOffset retainUntil) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            OccurredAt = now,
            ActorType = actor.Type,
            ActorId = actor.Id,
            Action = action,
            RecordType = recordType,
            RecordId = recordId,
            Purpose = purpose,
            CorrelationId = correlationId,
            RetainUntil = retainUntil,
        };
}
