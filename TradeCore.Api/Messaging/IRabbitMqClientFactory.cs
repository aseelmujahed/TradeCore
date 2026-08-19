namespace TradeCore.Api.Messaging;

public interface IRabbitMqClientFactory
{
    Task<IRabbitMqSession> CreateAsync(RabbitMqOptions options, CancellationToken cancellationToken);
}

public interface IRabbitMqSession : IAsyncDisposable
{
    Task DeclareQueueAsync(RabbitMqQueueDeclaration declaration, CancellationToken cancellationToken);

    Task PublishAsync(RabbitMqPublishedMessage message, CancellationToken cancellationToken);
}

public sealed record RabbitMqQueueDeclaration(
    string Name,
    bool Durable,
    bool Exclusive,
    bool AutoDelete);

public sealed record RabbitMqPublishedMessage(
    string RoutingKey,
    ReadOnlyMemory<byte> Body,
    string ContentType,
    bool Persistent,
    string MessageId);

public interface IRabbitMqConnectionService : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task PublishAsync(RabbitMqPublishedMessage message, CancellationToken cancellationToken);
}
