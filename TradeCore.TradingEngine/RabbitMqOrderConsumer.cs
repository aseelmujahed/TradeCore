using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

public sealed class RabbitMqOrderConsumer(
    IOptions<RabbitMqOptions> options,
    ReliableOrderDeliveryProcessor deliveryProcessor,
    RabbitMqOrderDeliveryTransport transport,
    ILogger<RabbitMqOrderConsumer> logger) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;
    private string? _consumerTag;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("RabbitMQ order consumption is disabled.");
            return;
        }

        var settings = options.Value;
        var factory = new ConnectionFactory { HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName, Password = settings.Password };
        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, true), stoppingToken);
        await DeclareTopologyAsync(settings, stoppingToken);
        transport.SetChannel(_channel);
        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => ProcessDeliveryAsync(delivery, stoppingToken);
        _consumerTag = await _channel.BasicConsumeAsync(settings.OrdersQueue, false, consumer, stoppingToken);
        logger.LogInformation("Consuming submitted orders from RabbitMQ queue {Queue}.", settings.OrdersQueue);

        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null && _consumerTag is not null) await _channel.BasicCancelAsync(_consumerTag, false, cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _connection?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        base.Dispose();
    }

    private async Task DeclareTopologyAsync(RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await _channel!.QueueDeclareAsync(settings.OrdersQueue, true, false, false, null, false, false, cancellationToken);
        await _channel.QueueDeclareAsync(
            RabbitMqOrderDeliveryTransport.RetryQueueName(settings.OrdersQueue), true, false, false,
            new Dictionary<string, object?>
            {
                ["x-message-ttl"] = settings.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = settings.OrdersQueue
            }, false, false, cancellationToken);
        await _channel.QueueDeclareAsync(
            RabbitMqOrderDeliveryTransport.DeadLetterQueueName(settings.OrdersQueue), true, false, false, null, false, false, cancellationToken);
    }

    private async Task ProcessDeliveryAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        var orderDelivery = new OrderDelivery(
            delivery.DeliveryTag, delivery.Body,
            RabbitMqOrderDeliveryTransport.GetAttempt(delivery.BasicProperties.Headers),
            Guid.TryParse(delivery.BasicProperties.MessageId, out var orderId) ? orderId : null);
        try
        {
            await deliveryProcessor.ProcessAsync(orderDelivery, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // No ACK here: a failed confirmed publish must not turn into message loss.
            logger.LogError(exception, "Delivery {DeliveryTag} was not acknowledged because failure handling did not complete.", delivery.DeliveryTag);
        }
    }
}
