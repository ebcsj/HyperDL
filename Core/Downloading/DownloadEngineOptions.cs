namespace HyperDL.Core;

/// <summary>
/// Tunable engine behaviour. Defaults are deliberately conservative and keep the
/// existing 64 MiB network-buffer budget.
/// </summary>
public sealed record DownloadEngineOptions
{
    public BufferMode BufferMode { get; init; } = BufferMode.Auto;

    /// <summary>
    /// Aggregate speed limit for the engine in KiB/s. Zero disables limiting.
    /// </summary>
    public int SpeedLimitKBps { get; init; }

    /// <summary>
    /// Allows an idle worker to split a large in-flight range in half.
    /// </summary>
    public bool DynamicSegmentation { get; init; } = true;

    /// <summary>
    /// Dynamic splitting is only used for files at least this large.
    /// </summary>
    public long DynamicSegmentationThresholdBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Hard cap on the number of ranges produced by cooperative splitting.
    /// </summary>
    public int MaximumDynamicSegmentCount { get; init; } = 128;

    /// <summary>
    /// Minimum number of bytes a segment must retain after a split. 4 MiB keeps
    /// HTTP request overhead and random writes under control.
    /// </summary>
    public long MinimumDynamicSegmentSize { get; init; } = DownloadEngine.MinimumSegmentSize;

    /// <summary>
    /// Number of attempts for a single range (including the first attempt).
    /// </summary>
    public int MaximumRetryAttempts { get; init; } = 4;

    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan RetryMaximumDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// A connection that produces no bytes for this long is treated as stalled and retried.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Optional per-host concurrency cap. Zero means the global limiter is the only cap.
    /// </summary>
    public int MaximumConnectionsPerHost { get; init; }

    public DownloadEngineOptions Validate()
    {
        if (MaximumDynamicSegmentCount is < 2 or > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumDynamicSegmentCount));
        }

        if (MinimumDynamicSegmentSize < 256 * 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumDynamicSegmentSize));
        }

        if (MaximumRetryAttempts is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRetryAttempts));
        }

        if (RetryBaseDelay < TimeSpan.Zero || RetryMaximumDelay < RetryBaseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryBaseDelay));
        }

        if (IdleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(IdleTimeout));
        }

        if (MaximumConnectionsPerHost is < 0 or > DownloadEngine.MaximumSegmentCount)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumConnectionsPerHost));
        }

        if (SpeedLimitKBps is < 0 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(SpeedLimitKBps));
        }

        return this;
    }
}
