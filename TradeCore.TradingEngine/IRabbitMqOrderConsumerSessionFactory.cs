using TradeCore.Messaging;

namespace TradeCore.TradingEngine;

public interface IRabbitMqOrderConsumerSessionFactory
{
    Task<IRabbitMqOrderConsumerSession> CreateAsync(
        RabbitMqOptions options,
        Func<OrderDelivery, CancellationToken, Task> processDelivery,
        CancellationToken cancellationToken);
}

public interface IRabbitMqOrderConsumerSession : IAsyncDisposable
{
    Task WaitForShutdownAsync(CancellationToken cancellationToken);

    Task CancelAsync(CancellationToken cancellationToken);
}
