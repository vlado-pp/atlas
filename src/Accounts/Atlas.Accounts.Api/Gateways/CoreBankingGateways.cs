using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Atlas.Accounts.Api.Gateways;

public sealed record OpenAccountCommand(string IdempotencyKey, string Market, string HolderFirstName, string HolderLastName, DateOnly HolderDateOfBirth);

public sealed record OrderCardCommand(string IdempotencyKey, string AccountNumber, string CardholderName);

/// <summary>Seam to the core-banking system (out of scope; the brief asks for a plausible seam). Must be idempotent per key.</summary>
public interface ICoreBankingGateway
{
    Task<string> OpenAccountAsync(OpenAccountCommand command, CancellationToken cancellationToken);
}

/// <summary>Seam to the card processor. Must be idempotent per key.</summary>
public interface ICardProcessorGateway
{
    Task<string> OrderCardAsync(OrderCardCommand command, CancellationToken cancellationToken);
}

public sealed class FakeGatewayOptions
{
    public const string SectionName = "FakeGateways";

    /// <summary>Fail this many calls per application before succeeding, to exercise redelivery.</summary>
    public int CoreBankingFailuresBeforeSuccess { get; set; }

    public int CardProcessorFailuresBeforeSuccess { get; set; }
}

public sealed class GatewayUnavailableException : Exception
{
    public GatewayUnavailableException()
    {
    }

    public GatewayUnavailableException(string message)
        : base(message)
    {
    }

    public GatewayUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>In-memory core banking: same key, same account number. Failure injection via configuration.</summary>
public sealed class FakeCoreBankingGateway(IOptions<FakeGatewayOptions> options) : ICoreBankingGateway
{
    private readonly ConcurrentDictionary<string, string> _accounts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
    private int _sequence;

    public Task<string> OpenAccountAsync(OpenAccountCommand command, CancellationToken cancellationToken)
    {
        if (_attempts.AddOrUpdate(command.IdempotencyKey, 1, (_, n) => n + 1) <= options.Value.CoreBankingFailuresBeforeSuccess)
        {
            throw new GatewayUnavailableException("Core banking unavailable (injected).");
        }

        var number = _accounts.GetOrAdd(
            command.IdempotencyKey,
            _ => string.Create(CultureInfo.InvariantCulture, $"{command.Market}00ATLS{Interlocked.Increment(ref _sequence):D10}"));
        return Task.FromResult(number);
    }
}

/// <summary>In-memory card processor: same key, same order reference.</summary>
public sealed class FakeCardProcessorGateway(IOptions<FakeGatewayOptions> options) : ICardProcessorGateway
{
    private readonly ConcurrentDictionary<string, string> _orders = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

    public Task<string> OrderCardAsync(OrderCardCommand command, CancellationToken cancellationToken)
    {
        if (_attempts.AddOrUpdate(command.IdempotencyKey, 1, (_, n) => n + 1) <= options.Value.CardProcessorFailuresBeforeSuccess)
        {
            throw new GatewayUnavailableException("Card processor unavailable (injected).");
        }

        return Task.FromResult(_orders.GetOrAdd(command.IdempotencyKey, _ => $"card_{Guid.NewGuid():N}"));
    }
}
