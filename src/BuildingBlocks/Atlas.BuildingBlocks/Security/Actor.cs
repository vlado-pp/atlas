namespace Atlas.BuildingBlocks.Security;

public enum ActorType
{
    Customer,
    Staff,
    Service,
    System,
}

/// <summary>
/// Who is performing an operation. Every access to customer data is attributed to one (GC-2026-0814 §2).
/// </summary>
public sealed record Actor(ActorType Type, string Id)
{
    public static Actor Service(string serviceName) => new(ActorType.Service, serviceName);

    public static Actor System(string component) => new(ActorType.System, component);

    public override string ToString() => $"{Type}:{Id}";
}
