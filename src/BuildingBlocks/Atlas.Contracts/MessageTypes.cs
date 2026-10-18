namespace Atlas.Contracts;

/// <summary>
/// Stable wire names for message types (used as the RabbitMQ routing key and the AMQP "type" property), so
/// renaming a C# class never silently breaks routing.
/// </summary>
public static class MessageTypes
{
    public const string AccountOpeningRequested = "onboarding.account-opening-requested.v1";
    public const string ApplicationStatusChanged = "onboarding.application-status-changed.v1";
    public const string CustomerAccountProvisioned = "accounts.customer-account-provisioned.v1";

    private static readonly Dictionary<Type, string> ByType = new()
    {
        [typeof(AccountOpeningRequested)] = AccountOpeningRequested,
        [typeof(ApplicationStatusChanged)] = ApplicationStatusChanged,
        [typeof(CustomerAccountProvisioned)] = CustomerAccountProvisioned,
    };

    public static string For(Type messageType) =>
        ByType.TryGetValue(messageType, out var name)
            ? name
            : throw new InvalidOperationException($"No wire name registered for {messageType.Name}.");

    public static string For<TMessage>() where TMessage : IIntegrationMessage => For(typeof(TMessage));
}
