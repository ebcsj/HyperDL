using HyperDL.Core;

namespace HyperDL.Tests;

public class GlobalConnectionLimiterTests
{
    [Fact]
    public async Task AcquireAsync_DoesNotExceedConfiguredGlobalLimit()
    {
        var limiter = new GlobalConnectionLimiter(2);
        using var first = await limiter.AcquireAsync();
        using var second = await limiter.AcquireAsync();
        var waiting = limiter.AcquireAsync().AsTask();

        Assert.Equal(2, limiter.ActiveCount);
        Assert.False(waiting.IsCompleted);

        first.Dispose();
        using var third = await waiting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, limiter.ActiveCount);
    }

    [Fact]
    public async Task AcquireAsync_CancellationDoesNotLeakCapacity()
    {
        var limiter = new GlobalConnectionLimiter(1);
        using var lease = await limiter.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        var waiting = limiter.AcquireAsync(cancellation.Token).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        lease.Dispose();
        Assert.Equal(0, limiter.ActiveCount);
        using var next = await limiter.AcquireAsync();
        Assert.Equal(1, limiter.ActiveCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void SetLimit_RejectsValuesOutsideGlobalMaximum(int limit)
    {
        var limiter = new GlobalConnectionLimiter();
        Assert.Throws<ArgumentOutOfRangeException>(() => limiter.SetLimit(limit));
    }
}
