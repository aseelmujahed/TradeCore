using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

public sealed class RabbitMqOrderConsumerSessionFactory(RabbitMqOrderDeliveryTransport transport) : IRabbitMqOrderConsumerSessionFactory
{
    public async Task<IRabbitMqOrderConsumerSession> CreateAsync(
        RabbitMqOptions settings,
        Func<OrderDelivery, CancellationToken, Task> processDelivery,
        CancellationToken cancellationToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = settings.HostName,
                Port = settings.Port,
                UserName = settings.UserName,
                Password = settings.Password
            };
            connection = await factory.CreateConnectionAsync(cancellationToken);
            channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), cancellationToken);
            await DeclareTopologyAsync(channel, settings, cancellationToken);
            transport.SetChannel(channel);
            await channel.BasicQosAsync(0, 1, false, cancellationToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => processDelivery(
                new OrderDelivery(
                    delivery.DeliveryTag,
                    delivery.Body,
                    RabbitMqOrderDeliveryTransport.GetAttempt(delivery.BasicProperties.Headers),
                    Guid.TryParse(delivery.BasicProperties.MessageId, out var orderId) ? orderId : null),
                cancellationToken);
            var consumerTag = await channel.BasicConsumeAsync(settings.OrdersQueue, false, consumer, cancellationToken);
            return new RabbitMqOrderConsumerSession(connection, channel, consumerTag, transport);
        }
        catch
        {
            if (channel is not null)
            {
                transport.ClearChannel(channel);
                await channel.DisposeAsync();
            }
            if (connection is not null) await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task DeclareTopologyAsync(IChannel channel, RabbitMqOptions settings, CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(settings.OrdersQueue, true, false, false, null, false, false, cancellationToken);
        await channel.QueueDeclareAsync(
            RabbitMqOrderDeliveryTransport.RetryQueueName(settings.OrdersQueue), true, false, false,
            new Dictionary<string, object?>
            {
                ["x-message-ttl"] = settings.RetryDelayMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = settings.OrdersQueue
            }, false, false, cancellationToken);
        await channel.QueueDeclareAsync(
            RabbitMqOrderDeliveryTransport.DeadLetterQueueName(settings.OrdersQueue), true, false, false, null, false, false, cancellationToken);
    }

    private sealed class RabbitMqOrderConsumerSession : IRabbitMqOrderConsumerSession
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;
        private readonly string _consumerTag;
        private readonly RabbitMqOrderDeliveryTransport _transport;
        private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncEventHandler<ShutdownEventArgs> _connectionShutdown;
        private readonly AsyncEventHandler<ShutdownEventArgs> _channelShutdown;

        public RabbitMqOrderConsumerSession(
            IConnection connection,
            IChannel channel,
            string consumerTag,
            RabbitMqOrderDeliveryTransport transport)
        {
            _connection = connection;
            _channel = channel;
            _consumerTag = consumerTag;
            _transport = transport;
            _connectionShutdown = OnShutdownAsync;
            _channelShutdown = OnShutdownAsync;
            _connection.ConnectionShutdownAsync += _connectionShutdown;
            _channel.ChannelShutdownAsync += _channelShutdown;
        }

        public Task WaitForShutdownAsync(CancellationToken cancellationToken) => _shutdown.Task.WaitAsync(cancellationToken);

        public Task CancelAsync(CancellationToken cancellationToken) => _channel.BasicCancelAsync(_consumerTag, false, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            _connection.ConnectionShutdownAsync -= _connectionShutdown;
            _channel.ChannelShutdownAsync -= _channelShutdown;
            _transport.ClearChannel(_channel);
            await _channel.DisposeAsync();
            await _connection.DisposeAsync();
        }

        private Task OnShutdownAsync(object _, ShutdownEventArgs __)
        {
            _shutdown.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
