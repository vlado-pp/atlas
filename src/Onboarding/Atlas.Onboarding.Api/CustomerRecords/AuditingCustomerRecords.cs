using System.Diagnostics;
using Atlas.BuildingBlocks.Markets;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Applications;

namespace Atlas.Onboarding.Api.CustomerRecords;

/// <summary>
/// Decorator that writes an access-log entry for every create and read of customer data, attributed to the actor
/// of the current market scope. Reads are logged and committed <em>before</em> the data is handed out: if the audit
/// write fails, the read fails.
/// </summary>
public sealed class AuditingCustomerRecords(ICustomerRecords inner, OnboardingDbContext db, MarketScope scope, TimeProvider clock) : ICustomerRecords
{
    private const string ApplicantRecord = "Applicant";
    private const string DocumentRecord = "ApplicantDocument";

    public void Add(Applicant applicant, AccessPurpose purpose)
    {
        inner.Add(applicant, purpose);
        Log(AccessAction.Create, ApplicantRecord, applicant.ApplicationId.Value, purpose, applicant.RetainUntil);
    }

    public async Task<Applicant?> FindAsync(ApplicationId applicationId, AccessPurpose purpose, CancellationToken cancellationToken)
    {
        var applicant = await inner.FindAsync(applicationId, purpose, cancellationToken);
        if (applicant is not null)
        {
            Log(AccessAction.Read, ApplicantRecord, applicationId.Value, purpose, applicant.RetainUntil);
            await db.SaveChangesAsync(cancellationToken);
        }

        return applicant;
    }

    public async Task<byte[]> ReadDocumentAsync(Applicant applicant, ApplicantDocument document, AccessPurpose purpose, CancellationToken cancellationToken)
    {
        Log(AccessAction.Read, DocumentRecord, $"{applicant.ApplicationId}/{document.Type}", purpose, applicant.RetainUntil);
        await db.SaveChangesAsync(cancellationToken);
        return await inner.ReadDocumentAsync(applicant, document, purpose, cancellationToken);
    }

    private void Log(AccessAction action, string recordType, string recordId, AccessPurpose purpose, DateTimeOffset retainUntil) =>
        db.AccessLog.Add(AccessLogEntry.Create(scope.Actor, action, recordType, recordId, purpose.ToString(), Activity.Current?.Id, clock.GetUtcNow(), retainUntil));
}
