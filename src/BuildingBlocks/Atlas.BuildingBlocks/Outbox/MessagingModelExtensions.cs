using Atlas.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.BuildingBlocks.Outbox;

public static class MessagingModelExtensions
{
    /// <summary>Adds the outbox and inbox tables to a service's schema.</summary>
    public static ModelBuilder AddOutboxAndInbox(this ModelBuilder modelBuilder, string schema)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("OutboxMessages", schema);
            b.HasKey(m => m.Id);
            b.Property(m => m.Type).HasMaxLength(200).IsRequired();
            b.Property(m => m.Market).HasMaxLength(2).IsUnicode(false).IsRequired();
            b.Property(m => m.Payload).IsRequired();
            b.Property(m => m.CorrelationId).HasMaxLength(200);
            b.Property(m => m.LastError).HasMaxLength(1000);
            b.HasIndex(m => new { m.SentAt, m.OccurredAt });
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("InboxMessages", schema);
            b.HasKey(m => new { m.MessageId, m.Consumer });
            b.Property(m => m.Consumer).HasMaxLength(200);
        });

        return modelBuilder;
    }

    public static void AddToOutbox<TMessage>(this DbContext db, TMessage message, DateTimeOffset now)
        where TMessage : IIntegrationMessage =>
        db.Set<OutboxMessage>().Add(OutboxMessage.Create(message, now));

    public static Task<bool> HasProcessedAsync(this DbContext db, Guid messageId, string consumer, CancellationToken cancellationToken) =>
        db.Set<InboxMessage>().AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, cancellationToken);

    public static void MarkProcessed(this DbContext db, Guid messageId, string consumer, DateTimeOffset now) =>
        db.Set<InboxMessage>().Add(InboxMessage.Create(messageId, consumer, now));
}
