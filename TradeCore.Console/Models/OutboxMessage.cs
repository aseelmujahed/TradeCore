namespace TradeCore.Console.Models;

/// <summary>
/// A durable instruction to publish a message after the database transaction commits.
/// </summary>
public sealed class OutboxMessage
{
    public const string ApiOwner = "Api";
    public const string OrderSubmittedMessageType = "OrderSubmitted";
    public const string TradingEngineOwner = "TradingEngine";
    public const string TradeExecutedMessageType = "TradeExecuted";
    public const string StockPriceUpdatedMessageType = "StockPriceUpdated";

    private OutboxMessage()
    {
    }

    public OutboxMessage(Guid id, Guid orderId, string messageType, string payload)
        : this(id, orderId, ApiOwner, messageType, payload)
    {
    }

    public OutboxMessage(Guid id, Guid orderId, string owner, string messageType, string payload)
    {
        if (id == Guid.Empty) throw new ArgumentException("Outbox message ID cannot be empty.", nameof(id));
        if (orderId == Guid.Empty) throw new ArgumentException("Order ID cannot be empty.", nameof(orderId));
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(owner));
        if (string.IsNullOrWhiteSpace(messageType)) throw new ArgumentException("Message type is required.", nameof(messageType));
        if (string.IsNullOrWhiteSpace(payload)) throw new ArgumentException("Payload is required.", nameof(payload));

        Id = id;
        OrderId = orderId;
        Owner = owner;
        MessageType = messageType;
        Payload = payload;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public string Owner { get; private set; } = ApiOwner;

    public string MessageType { get; private set; } = string.Empty;

    public string Payload { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public void MarkPublished(DateTime publishedAt)
    {
        if (publishedAt.Kind != DateTimeKind.Utc) throw new ArgumentException("Published time must be UTC.", nameof(publishedAt));

        PublishedAt = publishedAt;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        AttemptCount++;
        LastError = error;
    }
}
