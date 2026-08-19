using TradeCore.Messaging;

namespace TradeCore.Api.Messaging;

public interface IOrderMessagePublisher
{
    Task PublishAsync(OrderSubmittedMessage message, CancellationToken cancellationToken);
}
