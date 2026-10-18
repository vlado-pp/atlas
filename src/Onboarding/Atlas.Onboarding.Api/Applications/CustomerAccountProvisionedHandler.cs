using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Outbox;
using Atlas.Contracts;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Api.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Applications;

/// <summary>Accounts reports the account open and the card ordered: the application is approved (AC6).</summary>
public sealed class CustomerAccountProvisionedHandler(
    OnboardingDbContext db,
    OnboardingMetrics metrics,
    TimeProvider clock) : IMessageHandler<CustomerAccountProvisioned>
{
    private const string Consumer = "onboarding.customer-account-provisioned";

    public async Task HandleAsync(CustomerAccountProvisioned message, MessageContext context, CancellationToken cancellationToken)
    {
        if (await db.HasProcessedAsync(context.MessageId, Consumer, cancellationToken))
        {
            return;
        }

        var applicationId = ApplicationId.FromTrusted(message.ApplicationId);
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId, cancellationToken);
        var now = clock.GetUtcNow();

        var result = application.RecordAccountProvisioned(message.AccountId, message.CardOrderReference, now);
        if (!result.IsSuccess)
        {
            // Not retryable by waiting; after the delivery limit the message is dead-lettered for investigation.
            throw new InvalidOperationException(result.Error!.Message);
        }

        db.MarkProcessed(context.MessageId, Consumer, now);
        await db.SaveChangesAsync(cancellationToken);
        metrics.StatusReached(application, now);
    }
}
