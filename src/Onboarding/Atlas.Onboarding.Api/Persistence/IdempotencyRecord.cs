namespace Atlas.Onboarding.Api.Persistence;

/// <summary>
/// Remembers which application an <c>Idempotency-Key</c> produced, so a retried submit (dropped connection,
/// "train in a tunnel") returns the original answer instead of creating a second application.
/// </summary>
public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public string ActorId { get; private set; } = string.Empty;

    public string Key { get; private set; } = string.Empty;

    public string RequestSha256 { get; private set; } = string.Empty;

    public string ApplicationId { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public static IdempotencyRecord Create(string actorId, string key, string requestSha256, string applicationId, DateTimeOffset now) =>
        new() { ActorId = actorId, Key = key, RequestSha256 = requestSha256, ApplicationId = applicationId, CreatedAt = now };
}
