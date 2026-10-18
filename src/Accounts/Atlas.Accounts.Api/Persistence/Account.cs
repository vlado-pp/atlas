namespace Atlas.Accounts.Api.Persistence;

/// <summary>
/// Our record of an account opened in core banking for an onboarding application. Also the progress marker that
/// makes provisioning resumable: a redelivered message continues where the last attempt stopped.
/// </summary>
public sealed class Account
{
    private Account()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>One account per application; unique, so concurrent duplicate deliveries cannot open two.</summary>
    public string ApplicationId { get; private set; } = string.Empty;

    public string Market { get; private set; } = string.Empty;

    public string AccountNumber { get; private set; } = string.Empty;

    public DateTimeOffset OpenedAt { get; private set; }

    public string? CardOrderReference { get; private set; }

    public DateTimeOffset? CardOrderedAt { get; private set; }

    public static Account Open(string applicationId, string market, string accountNumber, DateTimeOffset now) =>
        new()
        {
            Id = Guid.CreateVersion7(now),
            ApplicationId = applicationId,
            Market = market,
            AccountNumber = accountNumber,
            OpenedAt = now,
        };

    public void RecordCardOrdered(string cardOrderReference, DateTimeOffset now)
    {
        CardOrderReference = cardOrderReference;
        CardOrderedAt = now;
    }
}
