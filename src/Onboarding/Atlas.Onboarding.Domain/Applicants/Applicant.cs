using Atlas.Onboarding.Domain.Applications;

namespace Atlas.Onboarding.Domain.Applicants;

public sealed record CustomerIdentifier(string Scheme, string Value);

public enum DocumentType
{
    Passport,
    IdCard,
    Selfie,
}

/// <summary>Metadata of a stored document. The image itself is in blob storage in the market partition (ADR-0009).</summary>
public sealed class ApplicantDocument
{
    private ApplicantDocument()
    {
    }

    public Guid Id { get; private set; }

    public DocumentType Type { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string ContentSha256 { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public static ApplicantDocument Create(Guid id, DocumentType type, string storageKey, string contentSha256, long sizeBytes) =>
        new() { Id = id, Type = type, StorageKey = storageKey, ContentSha256 = contentSha256, SizeBytes = sizeBytes };
}

/// <summary>
/// The applicant's personal data, kept apart from the workflow record so it can be erased without breaking the
/// retained audit trail (ADR-0011). Only reachable through the audited customer-record store (ADR-0010).
/// </summary>
public sealed class Applicant
{
    private readonly List<ApplicantDocument> _documents = [];

    private Applicant()
    {
    }

    public ApplicationId ApplicationId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public DateOnly DateOfBirth { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    /// <summary>Used for screening. Not the same as the market: MF residents may hold another nationality.</summary>
    public string Nationality { get; private set; } = string.Empty;

    public CustomerIdentifier Identifier { get; private set; } = null!;

    public DateTimeOffset RetainUntil { get; private set; }

    public IReadOnlyCollection<ApplicantDocument> Documents => _documents.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}";

    public static Applicant Create(
        Application application,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        string email,
        string phone,
        string nationality,
        CustomerIdentifier identifier,
        IEnumerable<ApplicantDocument> documents)
    {
        var applicant = new Applicant
        {
            ApplicationId = application.Id,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            Email = email,
            Phone = phone,
            Nationality = nationality,
            Identifier = identifier,
            RetainUntil = application.RetainUntil,
        };
        applicant._documents.AddRange(documents);
        return applicant;
    }

    public ApplicantDocument? IdentityDocument => _documents.FirstOrDefault(d => d.Type is DocumentType.Passport or DocumentType.IdCard);

    public ApplicantDocument? Selfie => _documents.FirstOrDefault(d => d.Type == DocumentType.Selfie);
}
