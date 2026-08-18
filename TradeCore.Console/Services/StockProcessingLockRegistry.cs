namespace TradeCore.Console.Services;

/// <summary>
/// Coordinates in-process matching and settlement for each stock.  A separate
/// semaphore is used for each active stock so unrelated order books can proceed
/// independently.
/// </summary>
public sealed class StockProcessingLockRegistry
{
    private readonly object _entriesLock = new();
    private readonly Dictionary<Guid, Entry> _entries = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(Guid stockId, CancellationToken cancellationToken = default)
    {
        if (stockId == Guid.Empty)
        {
            throw new ArgumentException("Stock ID cannot be empty.", nameof(stockId));
        }

        Entry entry;
        lock (_entriesLock)
        {
            if (!_entries.TryGetValue(stockId, out entry!))
            {
                entry = new Entry();
                _entries.Add(stockId, entry);
            }

            entry.ReferenceCount++;
        }

        try
        {
            await entry.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Releaser(this, stockId, entry);
        }
        catch
        {
            ReleaseReference(stockId, entry);
            throw;
        }
    }

    private void Release(Guid stockId, Entry entry)
    {
        entry.Semaphore.Release();
        ReleaseReference(stockId, entry);
    }

    private void ReleaseReference(Guid stockId, Entry entry)
    {
        lock (_entriesLock)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0 &&
                _entries.TryGetValue(stockId, out var currentEntry) &&
                ReferenceEquals(currentEntry, entry))
            {
                _entries.Remove(stockId);
                entry.Semaphore.Dispose();
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private StockProcessingLockRegistry? _owner;
        private readonly Guid _stockId;
        private readonly Entry _entry;

        public Releaser(StockProcessingLockRegistry owner, Guid stockId, Entry entry)
        {
            _owner = owner;
            _stockId = stockId;
            _entry = entry;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_stockId, _entry);
            return ValueTask.CompletedTask;
        }
    }
}
