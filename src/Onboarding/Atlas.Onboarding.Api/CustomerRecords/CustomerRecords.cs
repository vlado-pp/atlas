using Atlas.BuildingBlocks.Markets;
using Atlas.Onboarding.Api.Documents;
using Atlas.Onboarding.Api.Persistence;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Applications;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Onboarding.Api.CustomerRecords;

/// <summary>Why customer data is being accessed. Recorded with every access (GC-2026-0814 §2).</summary>
public enum AccessPurpose
{
    ApplicationSubmission,
    IdentityVerification,
    SanctionsScreening,
    AccountOpening,
    BranchIdentityCheck,
}

/// <summary>
/// The only way into applicant personal data and documents. Registered behind
/// <see cref="AuditingCustomerRecords"/>, so a new caller cannot forget to write the access log (ADR-0010).
/// </summary>
public interface ICustomerRecords
{
    /// <summary>Adds a new applicant to the current unit of work; saved with it.</summary>
    void Add(Applicant applicant, AccessPurpose purpose);

    Task<Applicant?> FindAsync(ApplicationId applicationId, AccessPurpose purpose, CancellationToken cancellationToken);

    Task<byte[]> ReadDocumentAsync(Applicant applicant, ApplicantDocument document, AccessPurpose purpose, CancellationToken cancellationToken);
}

public sealed class CustomerRecords(OnboardingDbContext db, IDocumentStore documents, MarketScope scope) : ICustomerRecords
{
    public void Add(Applicant applicant, AccessPurpose purpose) => db.Applicants.Add(applicant);

    public Task<Applicant?> FindAsync(ApplicationId applicationId, AccessPurpose purpose, CancellationToken cancellationToken) =>
        db.Applicants.SingleOrDefaultAsync(a => a.ApplicationId == applicationId, cancellationToken);

    public Task<byte[]> ReadDocumentAsync(Applicant applicant, ApplicantDocument document, AccessPurpose purpose, CancellationToken cancellationToken) =>
        documents.ReadAsync(scope.Market, document.StorageKey, cancellationToken);
}
