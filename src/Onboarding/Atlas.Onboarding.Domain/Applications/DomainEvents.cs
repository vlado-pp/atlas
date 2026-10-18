namespace Atlas.Onboarding.Domain.Applications;

public interface IDomainEvent
{
    ApplicationId ApplicationId { get; }

    DateTimeOffset OccurredAt { get; }
}

/// <summary>All checks passed and the activation gate is open: the account can be opened and the card ordered.</summary>
public sealed record AccountOpeningRequestedEvent(ApplicationId ApplicationId, DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>The status the customer sees has changed; drives the push-notification seam.</summary>
public sealed record CustomerFacingStatusChangedEvent(
    ApplicationId ApplicationId,
    CustomerFacingStatus Status,
    RejectionReason? Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
