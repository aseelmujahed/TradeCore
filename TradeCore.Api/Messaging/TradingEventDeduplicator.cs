using System.Collections.Concurrent;

namespace TradeCore.Api.Messaging;

/// <summary>Suppresses broker redeliveries after a notification was successfully broadcast.</summary>
public sealed class TradingEventDeduplicator
{
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _completed = new();

    public bool TryReserve(string eventType, Guid eventId)
    {
        var key = $"{eventType}:{eventId:N}";
        var now = DateTimeOffset.UtcNow;
        if (_completed.TryGetValue(key, out var completedAt) && now - completedAt < Retention)
        {
            return false;
        }

        return _completed.TryAdd(key, now) || _completed.TryUpdate(key, now, completedAt);
    }

    public void Release(string eventType, Guid eventId) =>
        _completed.TryRemove($"{eventType}:{eventId:N}", out _);
}
