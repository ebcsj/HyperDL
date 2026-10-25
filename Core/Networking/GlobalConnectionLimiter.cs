namespace HyperDL.Core;

public sealed class GlobalConnectionLimiter
{
    private readonly SemaphoreSlim _capacity;
    private int _limit;
    private int _active;

    public GlobalConnectionLimiter(int limit = DownloadEngine.MaximumSegmentCount)
    {
        if (limit is < 1 or > DownloadEngine.MaximumSegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        _limit = limit;
        _capacity = new SemaphoreSlim(DownloadEngine.MaximumSegmentCount);
    }

    public static GlobalConnectionLimiter Shared { get; } = new();

    public int Limit => Volatile.Read(ref _limit);
    public int ActiveCount => Volatile.Read(ref _active);

    public void SetLimit(int limit)
    {
        if (limit is < 1 or > DownloadEngine.MaximumSegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        Volatile.Write(ref _limit, limit);
    }

    public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await _capacity.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (Interlocked.Increment(ref _active) <= Limit)
            {
                return new Lease(this);
            }

            Interlocked.Decrement(ref _active);
            _capacity.Release();
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken).ConfigureAwait(false);
        }
    }

    private void Release()
    {
        Interlocked.Decrement(ref _active);
        _capacity.Release();
    }

    private sealed class Lease(GlobalConnectionLimiter owner) : IDisposable
    {
        private GlobalConnectionLimiter? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
