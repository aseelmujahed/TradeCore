namespace TradeCore.TradingEngine;

public abstract class OrderMessageProcessingException(string message, Guid? orderId = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public Guid? OrderId { get; } = orderId;
}

public sealed class InvalidOrderMessageException(string message, Exception? innerException = null)
    : OrderMessageProcessingException(message, null, innerException);

public sealed class PersistedOrderNotFoundException(Guid orderId)
    : OrderMessageProcessingException($"Submitted-order message references missing order {orderId}.", orderId);
