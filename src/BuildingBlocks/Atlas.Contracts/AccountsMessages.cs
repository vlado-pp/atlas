namespace Atlas.Contracts;

/// <summary>The account is open and the card ordered. Carries internal references only, never the account number.</summary>
public sealed record CustomerAccountProvisioned(
    string ApplicationId,
    string Market,
    Guid AccountId,
    string CardOrderReference,
    DateTimeOffset ProvisionedAt) : IIntegrationMessage;
