using Atlas.BuildingBlocks.Outbox;
using Atlas.Contracts;
using Atlas.Onboarding.Api.Applications;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Persistence;

public sealed class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : DbContext(options)
{
    public const string Schema = "onboarding";

    public DbSet<Application> Applications => Set<Application>();

    /// <summary>Personal data. Read it through <see cref="CustomerRecords.ICustomerRecords"/> only, so every access is audited.</summary>
    internal DbSet<Applicant> Applicants => Set<Applicant>();

    public DbSet<AccessLogEntry> AccessLog => Set<AccessLogEntry>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    /// <summary>
    /// Turns the aggregate's domain events into outbox rows in the same transaction as the state change
    /// (transactional outbox, ADR-0004), so no handler can forget to publish.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var applications = ChangeTracker.Entries<Application>().Select(e => e.Entity).Where(a => a.DomainEvents.Count > 0).ToList();
        foreach (var application in applications)
        {
            foreach (var domainEvent in application.DomainEvents)
            {
                AddIntegrationMessage(application, domainEvent);
            }

            application.ClearDomainEvents();
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OnboardingDbContext).Assembly);
        modelBuilder.AddOutboxAndInbox(Schema);
    }

    private void AddIntegrationMessage(Application application, IDomainEvent domainEvent)
    {
        switch (domainEvent)
        {
            case AccountOpeningRequestedEvent e:
                this.AddToOutbox(new AccountOpeningRequested(e.ApplicationId.Value, application.Market, e.OccurredAt), e.OccurredAt);
                break;
            case CustomerFacingStatusChangedEvent e:
                this.AddToOutbox(
                    new ApplicationStatusChanged(e.ApplicationId.Value, application.Market, PublicStatus.Of(e.Status), PublicStatus.ReasonOf(e.Status, e.Reason), e.OccurredAt),
                    e.OccurredAt);
                break;
            default:
                throw new InvalidOperationException($"No integration message mapped for {domainEvent.GetType().Name}.");
        }
    }
}
