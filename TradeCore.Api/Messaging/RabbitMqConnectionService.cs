using Microsoft.Extensions.Options;

namespace TradeCore.Api.Messaging;

public sealed class RabbitMqConnectionService(
    IOptions<RabbitMqOptions> options,
    IRabbitMqClientFactory clientFactory) : IRabbitMqConnectionService
{
    public static readonly RabbitMqQueueDeclaration OrdersQueueDeclaration = new(
        Name: string.Empty,
        Durable: true,
        Exclusive: false,
        AutoDelete: false);

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private IRabbitMqSession? _session;
    private bool _disposed;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_session is not null)
            {
                return;
            }

            var rabbitMqOptions = options.Value;
            var session = await clientFactory.CreateAsync(rabbitMqOptions, cancellationToken);
            try
            {
                var declaration = OrdersQueueDeclaration with { Name = rabbitMqOptions.OrdersQueue };
                await session.DeclareQueueAsync(declaration, cancellationToken);
                _session = session;
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task PublishAsync(RabbitMqPublishedMessage message, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await _session!.PublishAsync(message, cancellationToken);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _initializationLock.WaitAsync();
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync();
                _session = null;
            }
        }
        finally
        {
            _initializationLock.Release();
            _initializationLock.Dispose();
        }
    }
}
