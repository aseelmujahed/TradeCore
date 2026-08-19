namespace TradeCore.TradingEngine;

public interface IOrderMessageHandler
{
    Task ProcessAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken);
}
