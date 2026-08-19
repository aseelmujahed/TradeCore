using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

public sealed class RabbitMqOrderConsumer(
    IOptions<RabbitMqOptions> options,
    OrderMessageHandler orderMessageHandler,
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

        var rabbitMqOptions = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = rabbitMqOptions.HostName,
            Port = rabbitMqOptions.Port,
            UserName = rabbitMqOptions.UserName,
            Password = rabbitMqOptions.Password
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.QueueDeclareAsync(
            queue: rabbitMqOptions.OrdersQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: stoppingToken);
        await _channel.BasicQosAsync(0, 1, false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, delivery) => ProcessDeliveryAsync(delivery, stoppingToken);
        _consumerTag = await _channel.BasicConsumeAsync(
            queue: rabbitMqOptions.OrdersQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);
        logger.LogInformation("Consuming submitted orders from RabbitMQ queue {OrdersQueue}.", rabbitMqOptions.OrdersQueue);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null && _consumerTag is not null)
        {
            await _channel.BasicCancelAsync(_consumerTag, false, cancellationToken);
        }

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        if (_channel is not null)
        {
            _channel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_connection is not null)
        {
            _connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        base.Dispose();
    }

    private async Task ProcessDeliveryAsync(BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        try
        {
            await orderMessageHandler.ProcessAsync(delivery.Body, stoppingToken);
            await _channel!.BasicAckAsync(delivery.DeliveryTag, false, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            // Task 38 will add retry and dead-letter handling.  For now, log and
            // settle the delivery so a malformed or invalid message cannot stall this consumer.
            logger.LogError(exception, "Unable to process RabbitMQ delivery {DeliveryTag}.", delivery.DeliveryTag);
            await _channel!.BasicAckAsync(delivery.DeliveryTag, false, CancellationToken.None);
        }
    }
}
