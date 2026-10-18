using Atlas.Accounts.Api.Gateways;
using Atlas.Accounts.Api.Persistence;
using Atlas.BuildingBlocks.Messaging;
using Atlas.BuildingBlocks.Outbox;
using Atlas.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Accounts.Api.Provisioning;

/// <summary>
/// Opens the account and orders the card (AC6), then reports back. Each external step is idempotent per
/// application and its result is saved before the next, so a redelivered message resumes rather than repeats.
/// </summary>
public sealed class AccountOpeningRequestedHandler(
    AccountsDbContext db,
    OnboardingClient onboarding,
    ICoreBankingGateway coreBanking,
    ICardProcessorGateway cardProcessor,
    TimeProvider clock,
    ILogger<AccountOpeningRequestedHandler> logger) : IMessageHandler<AccountOpeningRequested>
{
    private const string Consumer = "accounts.account-opening-requested";

    private CustomerProfile? _profile;

    public async Task HandleAsync(AccountOpeningRequested message, MessageContext context, CancellationToken cancellationToken)
    {
        if (await db.HasProcessedAsync(context.MessageId, Consumer, cancellationToken))
        {
            return;
        }

        var account = await db.Accounts.SingleOrDefaultAsync(a => a.ApplicationId == message.ApplicationId, cancellationToken);
        if (account is null)
        {
            var profile = await ProfileAsync(message.ApplicationId, cancellationToken);
            var accountNumber = await coreBanking.OpenAccountAsync(
                new OpenAccountCommand(message.ApplicationId, message.Market, profile.FirstName, profile.LastName, profile.DateOfBirth),
                cancellationToken);

            account = Account.Open(message.ApplicationId, message.Market, accountNumber, clock.GetUtcNow());
            db.Accounts.Add(account);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Account {AccountId} opened for {ApplicationId}", account.Id, message.ApplicationId);
        }

        if (account.CardOrderReference is null)
        {
            var profile = await ProfileAsync(message.ApplicationId, cancellationToken);
            var cardOrder = await cardProcessor.OrderCardAsync(
                new OrderCardCommand(message.ApplicationId, account.AccountNumber, $"{profile.FirstName} {profile.LastName}"),
                cancellationToken);
            account.RecordCardOrdered(cardOrder, clock.GetUtcNow());
            logger.LogInformation("Card ordered for account {AccountId}", account.Id);
        }

        var now = clock.GetUtcNow();
        db.AddToOutbox(new CustomerAccountProvisioned(message.ApplicationId, message.Market, account.Id, account.CardOrderReference!, now), now);
        db.MarkProcessed(context.MessageId, Consumer, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<CustomerProfile> ProfileAsync(string applicationId, CancellationToken cancellationToken) =>
        _profile ??= await onboarding.GetCustomerProfileAsync(applicationId, cancellationToken);
}
