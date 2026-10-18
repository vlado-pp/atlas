using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atlas.Onboarding.Api.Workflow;

/// <summary>
/// The database is the scheduler (ADR-0004): each replica repeatedly claims due applications in every market and
/// advances them one step. Each application is its own unit of work, so one failure never affects another.
/// </summary>
public sealed class WorkflowRunner(
    IMarketScopeFactory scopes,
    TimeProvider clock,
    IOptions<WorkflowOptions> options,
    ILogger<WorkflowRunner> logger) : BackgroundService
{
    private static readonly Actor Workflow = Actor.Service("onboarding-workflow");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            foreach (var market in scopes.Markets)
            {
                try
                {
                    processed += await RunMarketAsync(market, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Workflow pass failed for market {Market}", market);
                }
            }

            if (processed == 0)
            {
                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
            }
        }
    }

    private async Task<int> RunMarketAsync(string market, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> claimed;
        await using (var scope = scopes.Begin(market, Workflow))
        {
            claimed = await scope.Get<WorkClaims>().ClaimDueAsync(clock.GetUtcNow(), options.Value.Lease, options.Value.BatchSize, cancellationToken);
        }

        foreach (var id in claimed)
        {
            using var logScope = logger.BeginScope(new Dictionary<string, object> { ["ApplicationId"] = id, ["Market"] = market });
            try
            {
                await using var scope = scopes.Begin(market, Workflow);
                await scope.Get<ApplicationWorkflow>().AdvanceAsync(ApplicationId.FromTrusted(id), cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else changed the application meanwhile (lease expired, or a message/staff action). Their write wins;
                // the application is due again if there is still work.
                logger.LogInformation("Application {ApplicationId} changed concurrently; step discarded", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The claim lease makes it due again later.
                logger.LogError(ex, "Advancing application {ApplicationId} failed", id);
            }
        }

        return claimed.Count;
    }
}
