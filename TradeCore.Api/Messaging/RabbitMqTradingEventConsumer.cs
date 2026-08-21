using System.Text.Json;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradeCore.Messaging;

namespace TradeCore.Api.Messaging;

/// <summary>Consumes worker-owned trading events and only acknowledges after SignalR succeeds.</summary>
public sealed class RabbitMqTradingEventConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    TradingEventDeduplicator deduplicator,
    ILogger<RabbitMqTradingEventConsumer> logger) : BackgroundService
{
    private const string AttemptHeader = "x-tradecore-attempt";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly List<string> _consumerTags = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("RabbitMQ trading-event consumption is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(options.Value, stoppingToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "RabbitMQ trading-event consumer could not connect; it will retry.");
                await DisposeResourcesAsync();
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task StartConsumingAsync(RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
        await DeclareTopologyAsync(settings, cancellationToken);
        await _channel.BasicQosAsync(0, 1, false, cancellationToken);

        _consumerTags.Add(await ConsumeAsync<TradeExecutedEvent>(settings.TradeExecutedQueue, "TradeExecuted", cancellationToken));
        _consumerTags.Add(await ConsumeAsync<StockPriceUpdatedEvent>(settings.StockPriceUpdatedQueue, "StockPriceUpdated", cancellationToken));
        logger.LogInformation("Consuming trading events from {TradeQueue} and {PriceQueue}.", settings.TradeExecutedQueue, settings.StockPriceUpdatedQueue);
    }

    private async Task<string> ConsumeAsync<TMessage>(string queue, string eventType, CancellationToken cancellationToken)
    {
        var consumer = new AsyncEventingBasicConsumer(_channel!);
        consumer.ReceivedAsync += (_, delivery) => ProcessDeliveryAsync<TMessage>(queue, eventType, delivery, cancellationToken);
        return await _channel!.BasicConsumeAsync(queue, false, consumer, cancellationToken);
    }

    private async Task ProcessDeliveryAsync<TMessage>(
        string queue,
        string eventType,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<TMessage>(delivery.Body.Span, SerializerOptions)
                ?? throw new InvalidDataException($"{eventType} message was empty.");
            var eventId = GetEventId(message) ?? throw new InvalidDataException($"{eventType} message has no event ID.");

            if (!deduplicator.TryReserve(eventType, eventId))
            {
                await _channel!.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
                return;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var handler = scope.ServiceProvider.GetRequiredService<TradingEventNotificationHandler>();
                switch (message)
                {
                    case TradeExecutedEvent tradeExecuted:
                        await handler.HandleAsync(tradeExecuted, cancellationToken);
                        break;
                    case StockPriceUpdatedEvent stockPriceUpdated:
                        await handler.HandleAsync(stockPriceUpdated, cancellationToken);
                        break;
                    default:
                        throw new InvalidDataException($"Unexpected {eventType} message type.");
                }

                await _channel!.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
            }
            catch
            {
                deduplicator.Release(eventType, eventId);
                throw;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (InvalidDataException exception)
        {
            await PublishFailureAndAcknowledgeAsync(queue, delivery, exception, cancellationToken);
        }
        catch (Exception exception)
        {
            await RetryOrDeadLetterAsync(queue, delivery, exception, cancellationToken);
        }
    }

    private async Task RetryOrDeadLetterAsync(string queue, BasicDeliverEventArgs delivery, Exception exception, CancellationToken cancellationToken)
    {
        var attempt = GetAttempt(delivery.BasicProperties.Headers);
        if (attempt >= options.Value.MaxProcessingAttempts)
        {
            await PublishFailureAndAcknowledgeAsync(queue, delivery, exception, cancellationToken);
            return;
        }

        logger.LogWarning(exception, "Trading-event delivery {DeliveryTag} failed; scheduling attempt {Attempt}.", delivery.DeliveryTag, attempt + 1);
        await PublishCopyAsync($"{queue}.retry", delivery, attempt + 1, null, cancellationToken);
        await _channel!.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
    }

    private async Task PublishFailureAndAcknowledgeAsync(string queue, BasicDeliverEventArgs delivery, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Trading-event delivery {DeliveryTag} is being sent to the dead-letter queue.", delivery.DeliveryTag);
        await PublishCopyAsync($"{queue}.dead-letter", delivery, GetAttempt(delivery.BasicProperties.Headers), exception, cancellationToken);
        await _channel!.BasicAckAsync(delivery.DeliveryTag, false, cancellationToken);
    }

    private Task PublishCopyAsync(string queue, BasicDeliverEventArgs delivery, int attempt, Exception? exception, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, object?> { [AttemptHeader] = attempt };
        if (exception is not null) headers["x-tradecore-failure-type"] = exception.GetType().Name;
        return _channel!.BasicPublishAsync(string.Empty, queue, true, new BasicProperties
        {
            ContentType = "application/json",
            Persistent = true,
            MessageId = delivery.BasicProperties.MessageId,
            Headers = headers
        }, delivery.Body, cancellationToken).AsTask();
    }

    private async Task DeclareTopologyAsync(RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        foreach (var queue in new[] { settings.TradeExecutedQueue, settings.StockPriceUpdatedQueue })
        {
            await _channel!.QueueDeclareAsync(queue, true, false, false, null, false, false, cancellationToken);
            await _channel.QueueDeclareAsync($"{queue}.retry", true, false, false, new Dictionary<string, object?>
            {
                ["x-message-ttl"] = settings.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = queue
            }, false, false, cancellationToken);
            await _channel.QueueDeclareAsync($"{queue}.dead-letter", true, false, false, null, false, false, cancellationToken);
        }
    }

    private static Guid? GetEventId<TMessage>(TMessage message) => message switch
    {
        TradeExecutedEvent tradeExecuted when tradeExecuted.EventId != Guid.Empty => tradeExecuted.EventId,
        StockPriceUpdatedEvent stockPriceUpdated when stockPriceUpdated.EventId != Guid.Empty => stockPriceUpdated.EventId,
        _ => null
    };

    private static int GetAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is not null && headers.TryGetValue(AttemptHeader, out var value))
        {
            return value switch
            {
                byte number => Math.Max(1, (int)number),
                short number => Math.Max(1, (int)number),
                int number => Math.Max(1, number),
                long number when number <= int.MaxValue => Math.Max(1, (int)number),
                _ => 1
            };
        }
        return 1;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null)
        {
            foreach (var consumerTag in _consumerTags)
            {
                await _channel.BasicCancelAsync(consumerTag, false, cancellationToken);
            }
        }
        await base.StopAsync(cancellationToken);
    }

    private async Task DisposeResourcesAsync()
    {
        _consumerTags.Clear();
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public override void Dispose()
    {
        DisposeResourcesAsync().GetAwaiter().GetResult();
        base.Dispose();
    }
}
