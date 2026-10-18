namespace Atlas.Onboarding.Domain.Markets;

public interface ICheckDigitAlgorithm
{
    bool IsValid(string identifier);
}

/// <summary>
/// Check-digit algorithms are code; which market uses which is configuration. Annex B says ME's algorithm differs
/// from MA/MB's but none of the algorithms were supplied, so every market uses <c>None</c> until they are
/// confirmed. Inventing them would reject real customers.
/// </summary>
public static class CheckDigitAlgorithms
{
    public const string None = "None";

    private static readonly Dictionary<string, ICheckDigitAlgorithm> Algorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        [None] = new NoCheckDigit(),
    };

    public static IReadOnlyCollection<string> Names => Algorithms.Keys;

    public static ICheckDigitAlgorithm Get(string name) =>
        Algorithms.TryGetValue(name, out var algorithm)
            ? algorithm
            : throw new ArgumentException($"Unknown check-digit algorithm '{name}'.", nameof(name));

    private sealed class NoCheckDigit : ICheckDigitAlgorithm
    {
        public bool IsValid(string identifier) => true;
    }
}
