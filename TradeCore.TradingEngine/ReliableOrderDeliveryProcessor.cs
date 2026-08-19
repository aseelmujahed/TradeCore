using Microsoft.Extensions.Options;
using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

public sealed record OrderDelivery(
    ulong DeliveryTag,
    ReadOnlyMemory<byte> Body,
    int Attempt,
    Guid? OrderId);

public interface IOrderMessageDeliveryTransport
{
    Task AcknowledgeAsync(OrderDelivery delivery, CancellationToken cancellationToken);
    Task ScheduleRetryAsync(OrderDelivery delivery, int nextAttempt, CancellationToken cancellationToken);
    Task DeadLetterAsync(OrderDelivery delivery, Exception exception, CancellationToken cancellationToken);
}

/// <summary>Owns ACK/retry/DLQ ordering; business processing remains in OrderMessageHandler.</summary>
public sealed class ReliableOrderDeliveryProcessor(
    IOrderMessageHandler orderMessageHandler,
    IOrderMessageDeliveryTransport transport,
    IOptions<RabbitMqOptions> options,
    ILogger<ReliableOrderDeliveryProcessor> logger)
{
    public async Task ProcessAsync(OrderDelivery delivery, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        try
        {
            logger.LogInformation(
                "Processing order {OrderId}, delivery {DeliveryTag}, attempt {Attempt} of {MaxAttempts} from {Queue}.",
                delivery.OrderId, delivery.DeliveryTag, delivery.Attempt, settings.MaxProcessingAttempts, settings.OrdersQueue);
            await orderMessageHandler.ProcessAsync(delivery.Body, cancellationToken);
            await transport.AcknowledgeAsync(delivery, cancellationToken);
            logger.LogInformation(
                "Order {OrderId} processed successfully on attempt {Attempt} of {MaxAttempts}.",
                delivery.OrderId, delivery.Attempt, settings.MaxProcessingAttempts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOrderMessageException exception)
        {
            await DeadLetterAndAcknowledgeAsync(delivery, exception, settings, cancellationToken);
        }
        catch (Exception exception) when (delivery.Attempt >= settings.MaxProcessingAttempts)
        {
            await DeadLetterAndAcknowledgeAsync(delivery, exception, settings, cancellationToken);
        }
        catch (Exception exception)
        {
            var nextAttempt = delivery.Attempt + 1;
            logger.LogWarning(
                exception,
                "Order {OrderId} processing failed on attempt {Attempt} of {MaxAttempts}; scheduling attempt {NextAttempt}.",
                delivery.OrderId, delivery.Attempt, settings.MaxProcessingAttempts, nextAttempt);
            await transport.ScheduleRetryAsync(delivery, nextAttempt, cancellationToken);
            await transport.AcknowledgeAsync(delivery, cancellationToken);
        }
    }

    private async Task DeadLetterAndAcknowledgeAsync(
        OrderDelivery delivery,
        Exception exception,
        RabbitMqOptions settings,
        CancellationToken cancellationToken)
    {
        logger.LogError(
            exception,
            "Order {OrderId} failed after attempt {Attempt} of {MaxAttempts}; moving delivery {DeliveryTag} to the dead-letter queue.",
            delivery.OrderId, delivery.Attempt, settings.MaxProcessingAttempts, delivery.DeliveryTag);
        await transport.DeadLetterAsync(delivery, exception, cancellationToken);
        await transport.AcknowledgeAsync(delivery, cancellationToken);
    }
}
