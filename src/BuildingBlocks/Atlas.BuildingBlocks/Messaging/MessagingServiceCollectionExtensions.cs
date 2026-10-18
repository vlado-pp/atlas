using Atlas.BuildingBlocks.Markets;
using Atlas.BuildingBlocks.Outbox;
using Atlas.BuildingBlocks.Security;
using Atlas.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<MessagingTopology>();
        services.AddSingleton<RabbitMqConnectionProvider>();
        services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();
        return services;
    }

    public static IServiceCollection AddOutboxDispatcher<TContext>(this IServiceCollection services, IConfiguration configuration)
        where TContext : DbContext
    {
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName));
        services.AddHostedService<OutboxDispatcher<TContext>>();
        return services;
    }

    public static IServiceCollection AddMessageConsumer<TMessage, THandler>(this IServiceCollection services, string queue, Actor consumerActor)
        where TMessage : class, IIntegrationMessage
        where THandler : class, IMessageHandler<TMessage>
    {
        services.AddScoped<IMessageHandler<TMessage>, THandler>();
        services.AddHostedService(sp => new RabbitMqConsumer<TMessage>(
            queue,
            consumerActor,
            sp.GetRequiredService<RabbitMqConnectionProvider>(),
            sp.GetRequiredService<MessagingTopology>(),
            sp.GetRequiredService<IMarketScopeFactory>(),
            sp.GetRequiredService<IOptions<RabbitMqOptions>>(),
            sp.GetRequiredService<ILogger<RabbitMqConsumer<TMessage>>>()));
        return services;
    }
}
