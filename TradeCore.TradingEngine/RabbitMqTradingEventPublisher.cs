using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

/// <summary>Publishes durable post-settlement events without introducing an API dependency.</summary>
public sealed class RabbitMqTradingEventPublisher(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqTradingEventPublisher> logger) : ITradingEventPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public Task PublishAsync(TradeExecutedEvent message, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.TradeExecutedQueue, message.EventId, message, cancellationToken);

    public Task PublishAsync(StockPriceUpdatedEvent message, CancellationToken cancellationToken) =>
        PublishAsync(options.Value.StockPriceUpdatedQueue, message.EventId, message, cancellationToken);

    private async Task PublishAsync<TMessage>(
        string queue,
        Guid eventId,
        TMessage message,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        var body = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        await _channel!.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queue,
            mandatory: true,
            basicProperties: new BasicProperties
            {
                ContentType = "application/json",
                Persistent = true,
                MessageId = eventId.ToString()
            },
            body: body,
            cancellationToken: cancellationToken);
        logger.LogInformation("RabbitMQ confirmed trading event {EventId} for queue {Queue}.", eventId, queue);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_channel is not null)
            {
                return;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            try
            {
                _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
                await _channel.QueueDeclareAsync(settings.TradeExecutedQueue, true, false, false, null, false, false, cancellationToken);
                await _channel.QueueDeclareAsync(settings.StockPriceUpdatedQueue, true, false, false, null, false, false, cancellationToken);
            }
            catch
            {
                await _connection.DisposeAsync();
                _connection = null;
                _channel = null;
                throw;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _initializationLock.WaitAsync();
        try
        {
            if (_channel is not null) await _channel.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
        finally
        {
            _initializationLock.Release();
            _initializationLock.Dispose();
        }
    }
}
