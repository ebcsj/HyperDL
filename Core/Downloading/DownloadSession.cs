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
    private DownloadSessionStatus _status = DownloadSessionStatus.Waiting;
    private CancellationTokenSource _pauseSource = new();
    private bool _pauseRequested;

    internal DownloadSession(
        DownloadMetadata metadata,
        string fileName,
        string finalPath,
        int connectionCount,
        IReadOnlyList<DownloadSegment> segments,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Metadata = metadata;
        FileName = fileName;
        FinalPath = finalPath;
        PartPath = finalPath + ".part";
        ConnectionCount = connectionCount;
        Segments = segments;
    }

    public DownloadMetadata Metadata { get; }
    public Guid Id { get; }
    public string FileName { get; }
    public string FinalPath { get; }
    public string PartPath { get; }
    public int ConnectionCount { get; }
    public IReadOnlyList<DownloadSegment> Segments { get; }
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

    public void EnsureSourceUnchanged(DownloadMetadata current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if ((Metadata.ContentLength.HasValue &&
             current.ContentLength.HasValue &&
             Metadata.ContentLength.Value != current.ContentLength.Value) ||
            (Metadata.ETag is not null &&
             current.ETag is not null &&
             !string.Equals(Metadata.ETag, current.ETag, StringComparison.Ordinal)) ||
            (Metadata.LastModified.HasValue &&
             current.LastModified.HasValue &&
             Metadata.LastModified.Value != current.LastModified.Value))
        {
            throw new InvalidOperationException(
                "遠端檔案已變更，為避免混合不同版本資料，無法繼續此下載。");
        }
    }
}
