using System.Text.Json;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Security;
using Atlas.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Atlas.BuildingBlocks.Messaging;

public sealed record MessageContext(Guid MessageId, string? CorrelationId);

public interface IMessageHandler<in TMessage>
    where TMessage : IIntegrationMessage
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Consumes one queue and dispatches each message to its handler inside a market scope.
/// Ack after the handler commits; on failure nack-and-requeue, and the quorum queue's delivery limit moves a
/// message that keeps failing to the dead-letter queue so it cannot block the queue (ADR-0004).
/// </summary>
public sealed class RabbitMqConsumer<TMessage>(
    string queue,
    Actor consumerActor,
    RabbitMqConnectionProvider connections,
    MessagingTopology topology,
    IMarketScopeFactory scopes,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConsumer<TMessage>> logger) : BackgroundService
    where TMessage : class, IIntegrationMessage
{
    private CancellationToken _stoppingToken;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connection = await connections.GetAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await channel.BasicQosAsync(0, options.Value.PrefetchCount, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnReceivedAsync(channel, delivery);
                await channel.BasicConsumeAsync(topology.QueueName(queue), autoAck: false, consumer, stoppingToken);

                logger.LogInformation("Consuming {Queue}", topology.QueueName(queue));
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Consumer for {Queue} failed to start; retrying", queue);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task OnReceivedAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId) || TryDeserialize(delivery) is not { } message)
        {
            logger.LogError("Rejecting malformed message on {Queue} (delivery tag {DeliveryTag})", queue, delivery.DeliveryTag);
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, _stoppingToken);
            return;
        }

        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["ApplicationId"] = message.ApplicationId,
            ["Market"] = message.Market,
            ["MessageId"] = messageId,
        });

        try
        {
            await using (var scope = scopes.Begin(message.Market, consumerActor))
            {
                var handler = scope.Get<IMessageHandler<TMessage>>();
                await handler.HandleAsync(message, new MessageContext(messageId, delivery.BasicProperties.CorrelationId), _stoppingToken);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, _stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Handling {MessageType} failed; requeueing (delivery limit {DeliveryLimit})", typeof(TMessage).Name, options.Value.DeliveryLimit);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        }
    }

    private TMessage? TryDeserialize(BasicDeliverEventArgs delivery)
    {
        try
        {
            return JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, AtlasJson.Options);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Message body on {Queue} is not a valid {MessageType}", queue, typeof(TMessage).Name);
            return null;
        }
    }
}
