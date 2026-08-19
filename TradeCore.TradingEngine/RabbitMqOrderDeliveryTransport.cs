using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

/// <summary>Publishes confirmed retry/DLQ copies before the original delivery is acknowledged.</summary>
public sealed class RabbitMqOrderDeliveryTransport(IOptions<RabbitMqOptions> options) : IOrderMessageDeliveryTransport
{
    public const string AttemptHeader = "x-tradecore-attempt";
    private IChannel? _channel;

    public static string RetryQueueName(string ordersQueue) => $"{ordersQueue}.retry";
    public static string DeadLetterQueueName(string ordersQueue) => $"{ordersQueue}.dead-letter";
    public void SetChannel(IChannel channel) => _channel = channel;

    public Task AcknowledgeAsync(OrderDelivery delivery, CancellationToken cancellationToken) => GetChannel().BasicAckAsync(delivery.DeliveryTag, false, cancellationToken).AsTask();
    public Task ScheduleRetryAsync(OrderDelivery delivery, int nextAttempt, CancellationToken cancellationToken) => PublishAsync(RetryQueueName(options.Value.OrdersQueue), delivery, nextAttempt, null, cancellationToken);
    public Task DeadLetterAsync(OrderDelivery delivery, Exception exception, CancellationToken cancellationToken) => PublishAsync(DeadLetterQueueName(options.Value.OrdersQueue), delivery, delivery.Attempt, exception, cancellationToken);

    public static int GetAttempt(IDictionary<string, object?>? headers)
    {
        if (headers is not null && headers.TryGetValue(AttemptHeader, out var value))
        {
            return value switch
            {
                byte number => Math.Max(1, (int)number), short number => Math.Max(1, (int)number),
                int number => Math.Max(1, number), long number when number <= int.MaxValue => Math.Max(1, (int)number), _ => 1
            };
        }
        return 1;
    }

    private async Task PublishAsync(string queue, OrderDelivery delivery, int attempt, Exception? exception, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, object?> { [AttemptHeader] = attempt };
        if (exception is not null)
        {
            headers["x-tradecore-failure-type"] = exception.GetType().Name;
            headers["x-tradecore-original-queue"] = options.Value.OrdersQueue;
        }
        await GetChannel().BasicPublishAsync(string.Empty, queue, true, new BasicProperties
        {
            ContentType = "application/json", Persistent = true, MessageId = delivery.OrderId?.ToString(), Headers = headers
        }, delivery.Body, cancellationToken);
    }

    private IChannel GetChannel() => _channel ?? throw new InvalidOperationException("RabbitMQ delivery transport is not initialized.");
}
