using TradeCore.Api.Messaging;

namespace TradeCore.Tests;

public sealed class TradingEventDeduplicatorTests
{
    [Fact]
    public void TryReserve_suppresses_a_redelivered_event_but_keeps_event_types_independent()
    {
        var deduplicator = new TradingEventDeduplicator();
        var eventId = Guid.NewGuid();

        Assert.True(deduplicator.TryReserve("TradeExecuted", eventId));
        Assert.False(deduplicator.TryReserve("TradeExecuted", eventId));
        Assert.True(deduplicator.TryReserve("StockPriceUpdated", eventId));
    }

    [Fact]
    public void Release_allows_a_failed_notification_to_be_retried()
    {
        var deduplicator = new TradingEventDeduplicator();
        var eventId = Guid.NewGuid();

        Assert.True(deduplicator.TryReserve("TradeExecuted", eventId));
        deduplicator.Release("TradeExecuted", eventId);

        Assert.True(deduplicator.TryReserve("TradeExecuted", eventId));
    }
}
