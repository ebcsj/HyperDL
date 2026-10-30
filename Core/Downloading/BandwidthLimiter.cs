using System.Diagnostics;

namespace HyperDL.Core;

/// <summary>
/// A shared leaky-bucket rate limiter. A single instance is used by one engine
/// so the configured speed limit applies to the sum of all active connections.
/// A limit of zero or less disables throttling.
/// </summary>
public sealed class BandwidthLimiter
{
    private readonly object _sync = new();
    private long _bytesPerSecond;
    private long _nextAvailableTimestamp;

    public BandwidthLimiter(int kilobytesPerSecond = 0)
    {
        SetLimit(kilobytesPerSecond);
    }

    public int LimitKBps => (int)Math.Min(int.MaxValue, Volatile.Read(ref _bytesPerSecond) / 1024);

    public void SetLimit(int kilobytesPerSecond)
    {
        var bytes = kilobytesPerSecond <= 0
            ? 0L
            : Math.Min((long)kilobytesPerSecond * 1024, long.MaxValue / 2);
        lock (_sync)
        {
            Volatile.Write(ref _bytesPerSecond, bytes);
            _nextAvailableTimestamp = 0;
        }
    }

    /// <summary>
    /// Reserves transmission time for <paramref name="bytes"/> and waits until
    /// that time. Concurrent callers are serialized by the bucket so the
    /// aggregate rate stays at or below the configured limit.
    /// </summary>
    public async ValueTask ThrottleAsync(int bytes, CancellationToken cancellationToken = default)
    {
        if (bytes <= 0)
        {
            return;
        }

        var limit = Volatile.Read(ref _bytesPerSecond);
        if (limit <= 0)
        {
            return;
        }

        long delayTimestamp;
        lock (_sync)
        {
            var now = Stopwatch.GetTimestamp();
            var duration = (long)(bytes / (double)limit * Stopwatch.Frequency);
            if (duration < 1)
            {
                duration = 1;
            }

            var start = Math.Max(now, _nextAvailableTimestamp);
            _nextAvailableTimestamp = start + duration;
            delayTimestamp = start - now;
        }

        if (delayTimestamp <= 0)
        {
            return;
        }

        var milliseconds = delayTimestamp * 1000d / Stopwatch.Frequency;
        if (milliseconds >= 1)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
