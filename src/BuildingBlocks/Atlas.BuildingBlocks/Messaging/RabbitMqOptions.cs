using System.ComponentModel.DataAnnotations;

namespace Atlas.BuildingBlocks.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 5672;

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    public string VirtualHost { get; set; } = "/";

    /// <summary>Prefix for every exchange and queue name; lets test runs use an isolated topology.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Deliveries before a message is dead-lettered (quorum queue <c>x-delivery-limit</c>).</summary>
    [Range(1, 100)]
    public int DeliveryLimit { get; set; } = 5;

    [Range(1, 1000)]
    public ushort PrefetchCount { get; set; } = 10;
}
