using System.Diagnostics;
using System.Text.Json;
using Atlas.Contracts;

namespace Atlas.BuildingBlocks.Outbox;

/// <summary>
/// A message waiting to be published. Written in the same transaction as the state change that caused it, so a
/// state change and its message can never be split (ADR-0004).
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage()
    {
    }

    public Guid Id { get; private set; }

    public string Type { get; private set; } = string.Empty;

    public string Market { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public string? CorrelationId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxMessage Create<TMessage>(TMessage message, DateTimeOffset now)
        where TMessage : IIntegrationMessage =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            Type = MessageTypes.For<TMessage>(),
            Market = message.Market,
            Payload = JsonSerializer.Serialize(message, AtlasJson.Options),
            CorrelationId = Activity.Current?.Id,
            OccurredAt = now,
        };

    public void MarkSent(DateTimeOffset now) => SentAt = now;

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}

/// <summary>Records that a consumer has processed a message, which makes redelivery a no-op.</summary>
public sealed class InboxMessage
{
    private InboxMessage()
    {
    }

    public Guid MessageId { get; private set; }

    public string Consumer { get; private set; } = string.Empty;

    public DateTimeOffset ProcessedAt { get; private set; }

    public static InboxMessage Create(Guid messageId, string consumer, DateTimeOffset now) =>
        new() { MessageId = messageId, Consumer = consumer, ProcessedAt = now };
}
