using System.Text.RegularExpressions;
using Atlas.Onboarding.Domain.Applicants;
using Atlas.Onboarding.Domain.Common;

namespace Atlas.Onboarding.Domain.Markets;

/// <summary>
/// Validation rule for one identifier scheme in one market (Annex B). Identifiers are validated, never used as keys
/// (Annex B note 2).
/// </summary>
public sealed class IdentifierRule
{
    private readonly Regex _pattern;
    private readonly ICheckDigitAlgorithm _checkDigit;

    public IdentifierRule(string scheme, string pattern, string checkDigitAlgorithm, bool isPassport)
    {
        Scheme = scheme;
        IsPassport = isPassport;
        _pattern = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        _checkDigit = CheckDigitAlgorithms.Get(checkDigitAlgorithm);
    }

    public string Scheme { get; }

    public bool IsPassport { get; }

    public Result<CustomerIdentifier> Validate(string field, string value)
    {
        if (!_pattern.IsMatch(value))
        {
            return Error.Validation(field, "identifier.format", $"{Scheme} has an invalid format.");
        }

        if (!_checkDigit.IsValid(value))
        {
            return Error.Validation(field, "identifier.check_digit", $"{Scheme} check digit is invalid.");
        }

        return new CustomerIdentifier(Scheme, value);
    }
}
