namespace HyperDL.Core;

public enum DownloadSessionStatus
{
    Waiting,
    Downloading,
    Pausing,
    Paused,
    Recoverable,
    Completed,
    Cancelled,
    Failed
}

public sealed class DownloadSession
{
    private readonly object _sync = new();
    private readonly IReadOnlyList<DownloadSegment> _initialSegments;
    private DownloadSessionStatus _status = DownloadSessionStatus.Waiting;
    private CancellationTokenSource _pauseSource = new();
    private bool _pauseRequested;

    internal DownloadSession(
        DownloadMetadata metadata,
        string fileName,
        string finalPath,
        int connectionCount,
        IReadOnlyList<DownloadSegment> segments,
        Guid? id = null,
        string? partPath = null)
    {
        Id = id ?? Guid.NewGuid();
        Metadata = metadata;
        FileName = fileName;
        FinalPath = finalPath;
        PartPath = string.IsNullOrWhiteSpace(partPath) ? finalPath + ".part" : partPath;
        ConnectionCount = connectionCount;
        _initialSegments = segments;
    }

    public DownloadMetadata Metadata { get; }
    public Guid Id { get; }
    public string FileName { get; }
    public string FinalPath { get; }
    public string PartPath { get; }
    public int ConnectionCount { get; }

    /// <summary>
    /// Returns the live segment map once the engine has started, otherwise the
    /// segments captured when the session was created or loaded.
    /// </summary>
    public IReadOnlyList<DownloadSegment> Segments =>
        Scheduler?.Snapshot() ?? _initialSegments;

    public DownloadSessionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
        internal set
        {
            lock (_sync)
            {
                _status = value;
            }
        }
    }

    internal object SyncRoot => _sync;
    internal SegmentScheduler? Scheduler { get; set; }
    internal BufferMode? BufferModeOverride { get; set; }
    internal bool? DynamicSegmentationOverride { get; set; }
    internal int ResolvedBufferSize { get; set; } = DownloadEngine.BalancedBufferSize;
    public string? RecoveryError { get; internal set; }

    public bool PauseRequested
    {
        get
        {
            lock (_sync)
            {
                return _pauseRequested;
            }
        }
    }

    internal CancellationToken PauseToken => _pauseSource.Token;

    public void Pause()
    {
        lock (_sync)
        {
            if (_status == DownloadSessionStatus.Downloading)
            {
                _pauseRequested = true;
                _status = DownloadSessionStatus.Pausing;
                _pauseSource.Cancel();
            }
        }
    }

    internal void PrepareResume()
    {
        lock (_sync)
        {
            if (_pauseRequested)
            {
                _pauseSource.Dispose();
                _pauseSource = new CancellationTokenSource();
                _pauseRequested = false;
            }
        }
    }

    /// <summary>
    /// Verifies that resume will not stitch two different remote representations
    /// together. Missing validators that existed originally count as a change.
    /// </summary>
    public void EnsureSourceUnchanged(DownloadMetadata current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var changed =
            Metadata.ContentLength is { } originalLength &&
            (current.ContentLength is not { } currentLength ||
             originalLength != currentLength) ||
            Metadata.ETag is not null &&
            !string.Equals(Metadata.ETag, current.ETag, StringComparison.Ordinal) ||
            Metadata.LastModified is { } originalModified &&
            (current.LastModified is not { } currentModified ||
             originalModified != currentModified);

        if (changed)
        {
            throw new SourceChangedException(
                "The remote file changed since the download was paused.");
        }
    }
}
