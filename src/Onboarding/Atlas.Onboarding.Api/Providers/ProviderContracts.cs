using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Verification;

namespace Atlas.Onboarding.Api.Providers;

public sealed record IdentityVerificationRequest(DocumentType DocumentType, byte[] DocumentImage, byte[] SelfieImage);

/// <summary>Identity verification in our terms. Implemented by the IDNow adapter.</summary>
public interface IIdentityVerificationProvider
{
    /// <exception cref="ProviderUnavailableException">The provider could not give an answer; retry later.</exception>
    Task<IdentityCheckReport> VerifyAsync(IdentityVerificationRequest request, CancellationToken cancellationToken);
}

public sealed record ScreeningRequest(string FullName, DateOnly DateOfBirth, string Nationality);

/// <summary>Sanctions and PEP screening in our terms. Implemented by the World-Check adapter.</summary>
public interface ISanctionsScreeningProvider
{
    /// <exception cref="ProviderUnavailableException">The provider could not give an answer; retry later.</exception>
    Task<ScreeningResult> ScreenAsync(ScreeningRequest request, CancellationToken cancellationToken);

    /// <summary>Returns the compliance officer's decision, or null while the case is still open.</summary>
    /// <exception cref="ProviderUnavailableException">The provider could not give an answer; retry later.</exception>
    Task<ComplianceReviewResolution?> GetCaseResolutionAsync(string caseId, CancellationToken cancellationToken);
}

/// <summary>
/// A provider gave no usable answer: down, timed out, circuit open, or a response we cannot interpret.
/// This is a reason to retry, never a decision about the customer (ADR-0006).
/// </summary>
public sealed class ProviderUnavailableException : Exception
{
    public ProviderUnavailableException()
    {
    }

    public ProviderUnavailableException(string message)
        : base(message)
    {
    }

    public ProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ProviderUnavailableException(string provider, string message, Exception? innerException = null)
        : base($"{provider}: {message}", innerException) => Provider = provider;

    public string Provider { get; } = string.Empty;
}

/// <summary>The provider answered, but not in the documented contract.</summary>
public sealed class ProviderContractException(string message) : Exception(message)
{
    public ProviderContractException()
        : this("Response did not match the provider contract.")
    {
    }

    public ProviderContractException(string message, Exception innerException)
        : this(message + " " + innerException.Message)
    {
    }
}
