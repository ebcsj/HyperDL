using System.Diagnostics;
using HyperDL.Core;

namespace HyperDL.Tests;

public class BandwidthLimiterTests
{
    [Fact]
    public void LimitKBps_ReflectsConfiguredLimit()
    {
        var limiter = new BandwidthLimiter(150);

        Assert.Equal(150, limiter.LimitKBps);
    }

    [Fact]
    public async Task Throttle_ZeroLimitReturnsImmediately()
    {
        var limiter = new BandwidthLimiter(0);
        var stopwatch = Stopwatch.StartNew();

        await limiter.ThrottleAsync(1024 * 1024);

        stopwatch.Stop();
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Throttle_EnforcesAggregateRate()
    {
        var limiter = new BandwidthLimiter(100); // 100 KiB/s
        var stopwatch = Stopwatch.StartNew();

        // The first reservation is immediate; the next two pay the bucket debt.
        await limiter.ThrottleAsync(100 * 1024);
        await limiter.ThrottleAsync(100 * 1024);
        await limiter.ThrottleAsync(100 * 1024);

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromSeconds(1.9),
            $"expected ~2 s for 300 KiB at 100 KiB/s, took {stopwatch.Elapsed}");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task SetLimit_DisablesThrottlingImmediately()
    {
        var limiter = new BandwidthLimiter(100);
        await limiter.ThrottleAsync(100 * 1024);

        limiter.SetLimit(0);
        var stopwatch = Stopwatch.StartNew();
        await limiter.ThrottleAsync(100 * 1024);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task ConcurrentThrottles_SerializeToSharedBucket()
    {
        var limiter = new BandwidthLimiter(100); // 100 KiB/s total
        var stopwatch = Stopwatch.StartNew();

        // 4 x 50 KiB: first is immediate, the remaining three queue behind the
        // bucket for ~0.5 s each.
        await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(async _ => await limiter.ThrottleAsync(50 * 1024)));

        stopwatch.Stop();
        Assert.True(
            stopwatch.Elapsed >= TimeSpan.FromSeconds(1.2),
            $"expected at least ~1.5 s for 200 KiB aggregate, took {stopwatch.Elapsed}");
    }
}
