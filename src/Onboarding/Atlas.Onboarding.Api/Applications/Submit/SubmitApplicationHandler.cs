using System.Security.Cryptography;
using System.Text.Json;
using Atlas.BuildingBlocks;
using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Persistence;
using Atlas.Onboarding.Api.CustomerRecords;
using Atlas.Onboarding.Api.Documents;
using Atlas.Onboarding.Api.Markets;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.Applications.Submit;

public abstract record SubmitOutcome;

public sealed record SubmitAccepted(ApplicationStatusResponse Application) : SubmitOutcome;

/// <summary>The same Idempotency-Key was used for a different request body.</summary>
public sealed record IdempotencyKeyReused : SubmitOutcome;

/// <summary>
/// Accepts an application: stores the documents, then writes the application, the applicant's personal data, the
/// access-log entry and the idempotency record in one transaction. Processing continues in the workflow (202).
/// </summary>
public sealed class SubmitApplicationHandler(
    OnboardingDbContext db,
    ICustomerRecords customerRecords,
    IDocumentStore documents,
    MarketPolicies policies,
    MarketScope scope,
    TimeProvider clock,
    ILogger<SubmitApplicationHandler> logger)
{
    public async Task<SubmitOutcome> HandleAsync(
        SubmitApplicationRequest request,
        ValidSubmission submission,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actor = scope.Actor;
        var requestHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, AtlasJson.Options)));

        var previous = await FindIdempotencyRecordAsync(actor.Id, idempotencyKey, cancellationToken);
        if (previous is not null)
        {
            return await ReplayAsync(previous, requestHash, cancellationToken);
        }

        var now = clock.GetUtcNow();
        var application = Application.Submit(ApplicationId.New(submission.Market, now), actor.Id, policies.Get(submission.Market), now);

        // Documents go to blob storage first. If the transaction below fails, the blobs are orphaned but unreferenced;
        // the reverse order could leave an application pointing at documents that do not exist.
        var storedDocuments = new List<ApplicantDocument>();
        foreach (var document in submission.Documents)
        {
            var documentId = Guid.CreateVersion7(now);
            var storageKey = $"{application.Id}/{documentId:N}";
            await documents.SaveAsync(submission.Market, storageKey, document.Content, cancellationToken);
            storedDocuments.Add(ApplicantDocument.Create(documentId, document.Type, storageKey, Convert.ToHexString(SHA256.HashData(document.Content)), document.Content.LongLength));
        }

        var applicant = Applicant.Create(
            application,
            submission.FirstName,
            submission.LastName,
            submission.DateOfBirth,
            submission.Email,
            submission.Phone,
            submission.Nationality,
            submission.Identifier,
            storedDocuments);

        db.Applications.Add(application);
        customerRecords.Add(applicant, AccessPurpose.ApplicationSubmission);
        db.IdempotencyRecords.Add(IdempotencyRecord.Create(actor.Id, idempotencyKey, requestHash, application.Id.Value, now));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // A concurrent request with the same key won the race; answer with its result.
            db.ChangeTracker.Clear();
            var winner = await FindIdempotencyRecordAsync(actor.Id, idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency conflict without a stored record.", ex);
            return await ReplayAsync(winner, requestHash, cancellationToken);
        }

        logger.LogInformation("Application {ApplicationId} submitted in {Market}", application.Id, application.Market);
        return new SubmitAccepted(ApplicationStatusResponse.From(application));
    }

    private Task<IdempotencyRecord?> FindIdempotencyRecordAsync(string actorId, string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.ActorId == actorId && r.Key == key, cancellationToken);

    private async Task<SubmitOutcome> ReplayAsync(IdempotencyRecord record, string requestHash, CancellationToken cancellationToken)
    {
        if (record.RequestSha256 != requestHash)
        {
            return new IdempotencyKeyReused();
        }

        var applicationId = ApplicationId.FromTrusted(record.ApplicationId);
        var application = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId, cancellationToken);
        return new SubmitAccepted(ApplicationStatusResponse.From(application));
    }
}
