using RabbitMQ.Client;

namespace Atlas.BuildingBlocks.Messaging;

public sealed record OutgoingMessage(Guid MessageId, string Type, string Market, ReadOnlyMemory<byte> Body, string? CorrelationId);

public interface IMessagePublisher
{
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Publishes with publisher confirms and <c>mandatory</c>: the call only returns once the broker has accepted
/// the message and routed it to a queue, so the outbox row is never marked sent for a lost message.
/// </summary>
public sealed class RabbitMqPublisher(RabbitMqConnectionProvider connections, MessagingTopology topology) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                MessageId = message.MessageId.ToString(),
                Type = message.Type,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                CorrelationId = message.CorrelationId,
                Headers = new Dictionary<string, object?> { ["market"] = message.Market },
            };

            await channel.BasicPublishAsync(topology.EventsExchange, message.Type, mandatory: true, properties, message.Body, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _lock.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        var connection = await connections.GetAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        return _channel;
    }
}
