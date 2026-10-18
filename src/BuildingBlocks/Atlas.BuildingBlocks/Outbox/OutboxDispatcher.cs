using System.Text;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.BuildingBlocks.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public int BatchSize { get; set; } = 50;
}

/// <summary>
/// Publishes unsent outbox rows from every market database. Rows are claimed with UPDLOCK/READPAST so several
/// replicas can run the dispatcher without publishing the same row twice in parallel. Delivery is still
/// at-least-once (a crash between publish and commit republishes), which is why every consumer is idempotent.
/// </summary>
public sealed class OutboxDispatcher<TContext>(
    IMarketScopeFactory scopes,
    IMessagePublisher publisher,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    private static readonly Actor Dispatcher = Actor.System("outbox-dispatcher");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            foreach (var market in scopes.Markets)
            {
                try
                {
                    published += await DispatchAsync(market, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Outbox dispatch failed for market {Market}", market);
                }
            }

            if (published == 0)
            {
                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
            }
        }
    }

    private async Task<int> DispatchAsync(string market, CancellationToken cancellationToken)
    {
        await using var scope = scopes.Begin(market, Dispatcher);
        var db = scope.Get<TContext>();
        var schema = db.Model.FindEntityType(typeof(OutboxMessage))!.GetSchema();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
#pragma warning disable EF1002 // Schema comes from the EF model and batch size from options, not from user input.
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw($"SELECT TOP ({options.Value.BatchSize}) * FROM [{schema}].[OutboxMessages] WITH (UPDLOCK, READPAST, ROWLOCK) WHERE [SentAt] IS NULL ORDER BY [OccurredAt]")
            .ToListAsync(cancellationToken);
#pragma warning restore EF1002

        var published = 0;
        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(
                    new OutgoingMessage(message.Id, message.Type, message.Market, Encoding.UTF8.GetBytes(message.Payload), message.CorrelationId),
                    cancellationToken);
                message.MarkSent(clock.GetUtcNow());
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop at the first failure: the broker is most likely unavailable and later rows would fail too.
                message.MarkFailed(ex.Message);
                logger.LogWarning(ex, "Publishing outbox message {MessageId} of type {MessageType} failed", message.Id, message.Type);
                break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return published;
    }
}
