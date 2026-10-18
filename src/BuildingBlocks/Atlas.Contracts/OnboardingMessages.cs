namespace Atlas.Contracts;

/// <summary>Command-like event: identity and screening passed, the activation gate is open, open the account and order the card.</summary>
public sealed record AccountOpeningRequested(
    string ApplicationId,
    string Market,
    DateTimeOffset RequestedAt) : IIntegrationMessage;

/// <summary>Published when the public status of an application changes. The push-notification seam subscribes to this.</summary>
public sealed record ApplicationStatusChanged(
    string ApplicationId,
    string Market,
    string Status,
    string? Reason,
    DateTimeOffset OccurredAt) : IIntegrationMessage;
