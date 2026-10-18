using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Atlas.BuildingBlocks.Messaging;

/// <summary>
/// One long-lived connection per process (the RabbitMQ recommendation), created lazily so the service starts
/// even while the broker is down. The client's automatic recovery re-establishes it after a drop.
/// </summary>
public sealed class RabbitMqConnectionProvider(
    IOptions<RabbitMqOptions> options,
    MessagingTopology topology,
    IHostEnvironment environment) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            return _connection;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is not null)
            {
                return _connection;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.Host,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password,
                VirtualHost = settings.VirtualHost,
                ClientProvidedName = environment.ApplicationName,
                AutomaticRecoveryEnabled = true,
            };

            var connection = await factory.CreateConnectionAsync(cancellationToken);
            await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
            {
                await topology.DeclareAsync(channel, cancellationToken);
            }

            _connection = connection;
            return connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _lock.Dispose();
    }
}
