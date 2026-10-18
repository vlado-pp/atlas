using Atlas.Contracts;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Atlas.BuildingBlocks.Messaging;

/// <summary>
/// The whole broker topology in one place. Every service declares all of it on connect (declarations are
/// idempotent), so a message published before its consumer has ever started is still queued, not dropped.
/// </summary>
public sealed class MessagingTopology(IOptions<RabbitMqOptions> options)
{
    public const string AccountOpeningQueue = "accounts.account-opening-requested";
    public const string AccountProvisionedQueue = "onboarding.customer-account-provisioned";
    public const string StatusNotificationsQueue = "notifications.application-status-changed";

    private static readonly QueueDefinition[] Queues =
    [
        new(AccountOpeningQueue, MessageTypes.AccountOpeningRequested, MaxLength: null),
        new(AccountProvisionedQueue, MessageTypes.CustomerAccountProvisioned, MaxLength: null),

        // Seam for a push-notification service (ADR-0003). Capped so it cannot grow without bound locally.
        new(StatusNotificationsQueue, MessageTypes.ApplicationStatusChanged, MaxLength: 10_000),
    ];

    private string Prefix => options.Value.Prefix;

    public string EventsExchange => $"{Prefix}atlas.events";

    public string DeadLetterExchange => $"{Prefix}atlas.dead-letter";

    public string QueueName(string queue) => Prefix + queue;

    public async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        foreach (var queue in Queues)
        {
            var name = QueueName(queue.Name);
            var arguments = new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-delivery-limit"] = options.Value.DeliveryLimit,
                ["x-dead-letter-exchange"] = DeadLetterExchange,
            };
            if (queue.MaxLength is { } maxLength)
            {
                arguments["x-max-length"] = maxLength;
                arguments["x-overflow"] = "drop-head";
            }

            await channel.QueueDeclareAsync(name, durable: true, exclusive: false, autoDelete: false, arguments, cancellationToken: cancellationToken);
            await channel.QueueBindAsync(name, EventsExchange, queue.RoutingKey, cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync($"{name}.dead-letter", durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
            await channel.QueueBindAsync($"{name}.dead-letter", DeadLetterExchange, queue.RoutingKey, cancellationToken: cancellationToken);
        }
    }

    /// <summary>Removes the topology; used by test runs with a unique prefix.</summary>
    public async Task DeleteAsync(IChannel channel, CancellationToken cancellationToken)
    {
        foreach (var queue in Queues)
        {
            await channel.QueueDeleteAsync(QueueName(queue.Name), cancellationToken: cancellationToken);
            await channel.QueueDeleteAsync($"{QueueName(queue.Name)}.dead-letter", cancellationToken: cancellationToken);
        }

        await channel.ExchangeDeleteAsync(EventsExchange, cancellationToken: cancellationToken);
        await channel.ExchangeDeleteAsync(DeadLetterExchange, cancellationToken: cancellationToken);
    }

    private sealed record QueueDefinition(string Name, string RoutingKey, int? MaxLength);
}
