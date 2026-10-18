namespace Atlas.Contracts;

/// <summary>
/// A message that crosses a service boundary. Every message names the market partition it belongs to so the
/// consumer can open the right database (ADR-0009). Messages never carry personal data.
/// </summary>
public interface IIntegrationMessage
{
    string ApplicationId { get; }

    string Market { get; }
}
