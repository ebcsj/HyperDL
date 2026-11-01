namespace HyperDL.Core;

/// <summary>
/// A resizable concurrency gate. Unlike <see cref="SemaphoreSlim"/>, the limit
/// can be lowered or raised at runtime, which keeps the settings dialog honest.
/// </summary>
internal sealed class ConcurrencyGate
{
    private readonly object _sync = new();
    private readonly Queue<Waiter> _waiters = new();
    private int _limit;
    private int _active;

    public ConcurrencyGate(int limit)
    {
        _limit = Math.Max(1, limit);
    }

    public int Active
    {
        get
        {
            lock (_sync)
            {
                return _active;
            }
        }
    }

    public void SetLimit(int limit)
    {
        lock (_sync)
        {
            _limit = Math.Max(1, limit);
            Pump();
        }
    }

    public Task<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_active < _limit)
            {
                _active++;
                return Task.FromResult(new Lease(this));
            }

            var waiter = new Waiter(cancellationToken);
            _waiters.Enqueue(waiter);
            if (cancellationToken.CanBeCanceled)
            {
                cancellationToken.Register(() => waiter.Completion.TrySetCanceled(cancellationToken));
            }

            return waiter.Completion.Task;
        }
    }

    private void Pump()
    {
        while (_active < _limit && _waiters.Count > 0)
        {
            var waiter = _waiters.Dequeue();
            if (waiter.Completion.Task.IsCompleted)
            {
                continue;
            }

            _active++;
            waiter.Completion.TrySetResult(new Lease(this));
        }
    }

    private void Release()
    {
        lock (_sync)
        {
            if (_active > 0)
            {
                _active--;
            }

            Pump();
        }
    }

    private sealed class Waiter(CancellationToken cancellationToken)
    {
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public TaskCompletionSource<Lease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class Lease : IDisposable
    {
        private ConcurrencyGate? _gate;

        internal Lease(ConcurrencyGate gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.Release();
        }
    }
}
