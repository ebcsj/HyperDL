using System.ComponentModel;
using System.Runtime.CompilerServices;
using HyperDL.Localization;

namespace HyperDL.Core;

/// <summary>
/// One row in the download queue. Implements <see cref="INotifyPropertyChanged"/>
/// so the WPF view can bind directly without rebuilding rows on every tick.
/// </summary>
public sealed class DownloadJob : INotifyPropertyChanged
{
    private readonly object _sync = new();
    private readonly Guid _jobId;
    private DownloadMetadata _metadata;
    private DownloadSession? _session;
    private string _fileName;
    private Uri _sourceUri;
    private DownloadSessionStatus _status;
    private long _bytesDownloaded;
    private long? _totalBytes;
    private double _bytesPerSecond;
    private double? _percentage;
    private int _segmentCount;
    private int _activeConnections;
    private string? _errorMessage;
    private DownloadErrorKind? _errorKind;
    private string? _targetPath;

    public DownloadJob(
        DownloadMetadata metadata,
        DownloadSession? session,
        int requestedConnections,
        BufferMode bufferMode,
        Guid? id = null)
    {
        _metadata = metadata;
        _session = session;
        _jobId = session?.Id ?? id ?? Guid.NewGuid();
        RequestedConnections = requestedConnections;
        BufferMode = bufferMode;
        _status = session?.Status ?? DownloadSessionStatus.Waiting;
        _totalBytes = metadata.ContentLength;
        _segmentCount = session?.ConnectionCount ?? 1;
        _activeConnections = 0;
        if (session is not null)
        {
            lock (session.SyncRoot)
            {
                _bytesDownloaded = session.Segments.Sum(segment => segment.BytesDownloaded);
                _segmentCount = session.Segments.Count;
            }
        }

        _fileName = session?.FileName ?? metadata.FileName;
        _sourceUri = metadata.OriginalUri ?? metadata.FinalUri;
        if (_totalBytes is > 0)
        {
            _percentage = Math.Clamp(_bytesDownloaded * 100d / _totalBytes.Value, 0, 100);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id => _session?.Id ?? _jobId;

    public string FileName
    {
        get
        {
            lock (_sync)
            {
                return _fileName;
            }
        }
    }

    public DownloadMetadata Metadata
    {
        get
        {
            lock (_sync)
            {
                return _metadata;
            }
        }
    }

    public DownloadSession? Session
    {
        get
        {
            lock (_sync)
            {
                return _session;
            }
        }
    }

    public Uri SourceUri
    {
        get
        {
            lock (_sync)
            {
                return _sourceUri;
            }
        }
    }

    public int RequestedConnections { get; }
    public BufferMode BufferMode { get; }

    public DownloadSessionStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    public string StatusResourceKey => Status switch
    {
        DownloadSessionStatus.Waiting => "StatusQueued",
        DownloadSessionStatus.Downloading => "StatusDownloading",
        DownloadSessionStatus.Pausing => "StatusPausing",
        DownloadSessionStatus.Paused => "StatusPaused",
        DownloadSessionStatus.Recoverable => "StatusRecoverable",
        DownloadSessionStatus.Completed => "StatusCompleted",
        DownloadSessionStatus.Cancelled => "StatusCancelled",
        DownloadSessionStatus.Failed => "StatusFailed",
        _ => "StatusUnknown"
    };

    public string StatusText => LocalizationManager.GetString(StatusResourceKey);

    public long BytesDownloaded
    {
        get
        {
            lock (_sync)
            {
                return _bytesDownloaded;
            }
        }
    }

    public long? TotalBytes
    {
        get
        {
            lock (_sync)
            {
                return _totalBytes;
            }
        }
    }

    public double BytesPerSecond
    {
        get
        {
            lock (_sync)
            {
                return _bytesPerSecond;
            }
        }
    }

    public double? Percentage
    {
        get
        {
            lock (_sync)
            {
                return _percentage;
            }
        }
    }

    public int ConnectionCount
    {
        get
        {
            lock (_sync)
            {
                return Math.Max(_activeConnections, _segmentCount);
            }
        }
    }

    public int ActiveConnections
    {
        get
        {
            lock (_sync)
            {
                return _activeConnections;
            }
        }
    }

    public int SegmentCount
    {
        get
        {
            lock (_sync)
            {
                return _segmentCount;
            }
        }
    }

    public string? ErrorMessage
    {
        get
        {
            lock (_sync)
            {
                return _errorMessage;
            }
        }
    }

    public DownloadErrorKind? ErrorKind
    {
        get
        {
            lock (_sync)
            {
                return _errorKind;
            }
        }
    }

    public string? TargetPath
    {
        get
        {
            lock (_sync)
            {
                return _targetPath;
            }
        }
    }

    public bool IsResumable => Session is not null;
    public bool CanPause => Status is DownloadSessionStatus.Downloading or DownloadSessionStatus.Waiting;
    public bool CanResume => Status is DownloadSessionStatus.Paused or
        DownloadSessionStatus.Recoverable or
        DownloadSessionStatus.Cancelled or
        DownloadSessionStatus.Failed;
    public bool CanCancel => Status is DownloadSessionStatus.Downloading or
        DownloadSessionStatus.Waiting or
        DownloadSessionStatus.Pausing or
        DownloadSessionStatus.Paused;
    public bool CanRetry => Status is DownloadSessionStatus.Failed or
        DownloadSessionStatus.Cancelled or
        DownloadSessionStatus.Recoverable;
    public bool IsCompleted => Status == DownloadSessionStatus.Completed;
    public bool IsActive => Status is DownloadSessionStatus.Downloading or
        DownloadSessionStatus.Pausing or
        DownloadSessionStatus.Waiting;

    public string SizeText => TotalBytes is { } total ? FormatBytes(total) : "—";

    public string ProgressText => Percentage is { } percentage
        ? $"{percentage:0.#}%"
        : FormatBytes(BytesDownloaded);

    public string SpeedText => $"{(BytesPerSecond > 0 ? FormatBytes(BytesPerSecond) : "0 B")}/s";

    public string EtaText
    {
        get
        {
            if (TotalBytes is not { } total || BytesPerSecond <= 0)
            {
                return "—";
            }

            var remaining = Math.Max(0, total - BytesDownloaded);
            return FormatDuration(TimeSpan.FromSeconds(remaining / BytesPerSecond));
        }
    }

    public string BufferModeResourceKey => BufferMode.ToResourceKey();

    public string BufferModeText => LocalizationManager.GetString(BufferModeResourceKey);

    public string ErrorText => ErrorKind is { } kind
        ? LocalizationManager.GetString(kind.ToResourceKey())
        : ErrorMessage ?? string.Empty;

    public string ErrorDetailsText => ErrorKind is { } kind && ErrorMessage is { } message
        ? string.Format(
            LocalizationManager.GetString("ErrorDetailFormat"),
            LocalizationManager.GetString(kind.ToResourceKey()),
            message)
        : ErrorMessage ?? LocalizationManager.GetString("ErrorUnknown");

    internal CancellationTokenSource? Cancellation { get; set; }
    internal Task? RunTask { get; set; }
    internal bool NeedsSourceCheck { get; set; }
    internal bool PauseRequested { get; set; }
    internal bool CancelRequested { get; set; }

    internal void ApplyMetadata(DownloadMetadata metadata)
    {
        lock (_sync)
        {
            _metadata = metadata;
            if (_session is null)
            {
                _fileName = metadata.FileName;
                _sourceUri = metadata.OriginalUri ?? metadata.FinalUri;
                _totalBytes = metadata.ContentLength;
                _percentage = metadata.ContentLength is > 0
                    ? Math.Clamp(_bytesDownloaded * 100d / metadata.ContentLength.Value, 0, 100)
                    : null;
            }
        }

        Raise(nameof(Metadata));
        Raise(nameof(FileName));
        Raise(nameof(SourceUri));
        Raise(nameof(TotalBytes));
        Raise(nameof(SizeText));
        Raise(nameof(Percentage));
        Raise(nameof(ProgressText));
    }

    internal void AttachSession(DownloadSession session)
    {
        lock (_sync)
        {
            _session = session;
            lock (session.SyncRoot)
            {
                _segmentCount = session.Segments.Count;
                _bytesDownloaded = session.Segments.Sum(segment => segment.BytesDownloaded);
            }
        }

        Raise(nameof(Session));
        Raise(nameof(Id));
        Raise(nameof(IsResumable));
        Raise(nameof(SegmentCount));
        Raise(nameof(ConnectionCount));
        Raise(nameof(BytesDownloaded));
        Raise(nameof(ProgressText));
    }

    internal void UpdateProgress(DownloadProgress progress)
    {
        var changed = new List<string>();
        lock (_sync)
        {
            if (_bytesDownloaded != progress.BytesDownloaded)
            {
                _bytesDownloaded = progress.BytesDownloaded;
                changed.Add(nameof(BytesDownloaded));
            }

            if (_totalBytes != progress.TotalBytes)
            {
                _totalBytes = progress.TotalBytes;
                changed.Add(nameof(TotalBytes));
            }

            if (Math.Abs(_bytesPerSecond - progress.BytesPerSecond) > 1)
            {
                _bytesPerSecond = progress.BytesPerSecond;
                changed.Add(nameof(BytesPerSecond));
            }

            if (_percentage != progress.Percentage)
            {
                _percentage = progress.Percentage;
                changed.Add(nameof(Percentage));
            }

            if (_segmentCount != progress.SegmentCount)
            {
                _segmentCount = progress.SegmentCount;
                changed.Add(nameof(SegmentCount));
            }

            if (_activeConnections != progress.ActiveConnectionCount)
            {
                _activeConnections = progress.ActiveConnectionCount;
                changed.Add(nameof(ActiveConnections));
            }

            if (_status == DownloadSessionStatus.Waiting)
            {
                _status = DownloadSessionStatus.Downloading;
                changed.Add(nameof(Status));
                changed.Add(nameof(StatusResourceKey));
            }
        }

        RaiseDerived(changed);
    }

    internal void SetStatus(DownloadSessionStatus status)
    {
        lock (_sync)
        {
            if (_status == status)
            {
                return;
            }

            _status = status;
            if (status == DownloadSessionStatus.Completed)
            {
                _percentage = 100;
                _bytesPerSecond = 0;
            }
        }

        Raise(nameof(Status));
        Raise(nameof(StatusResourceKey));
        Raise(nameof(StatusText));
        Raise(nameof(Percentage));
        Raise(nameof(BytesPerSecond));
        Raise(nameof(ProgressText));
        Raise(nameof(SpeedText));
        Raise(nameof(EtaText));
        Raise(nameof(CanPause));
        Raise(nameof(CanResume));
        Raise(nameof(CanCancel));
        Raise(nameof(CanRetry));
        Raise(nameof(IsCompleted));
        Raise(nameof(IsActive));
    }

    internal void SetError(Exception exception, DownloadSessionStatus status)
    {
        lock (_sync)
        {
            _errorMessage = exception.Message;
            _errorKind = DownloadErrorClassifier.Classify(exception);
            _status = status;
            _bytesPerSecond = 0;
        }

        Raise(nameof(ErrorMessage));
        Raise(nameof(ErrorText));
        Raise(nameof(ErrorDetailsText));
        Raise(nameof(ErrorKind));
        SetStatus(status);
    }

    internal void SetTargetPath(string path)
    {
        lock (_sync)
        {
            _targetPath = path;
        }

        Raise(nameof(TargetPath));
    }

    internal void ClearError()
    {
        lock (_sync)
        {
            _errorMessage = null;
            _errorKind = null;
        }

        Raise(nameof(ErrorMessage));
        Raise(nameof(ErrorText));
        Raise(nameof(ErrorDetailsText));
        Raise(nameof(ErrorKind));
    }

    private void RaiseDerived(IReadOnlyCollection<string> changed)
    {
        foreach (var property in changed)
        {
            Raise(property);
        }

        if (changed.Count == 0)
        {
            return;
        }

        Raise(nameof(SizeText));
        Raise(nameof(ProgressText));
        Raise(nameof(SpeedText));
        Raise(nameof(EtaText));
        Raise(nameof(ConnectionCount));
        Raise(nameof(ActiveConnections));
        Raise(nameof(SegmentCount));
        Raise(nameof(StatusText));
    }

    /// <summary>
    /// Re-raises every localized display property after a language change.
    /// </summary>
    public void RefreshLocalization()
    {
        Raise(nameof(StatusText));
        Raise(nameof(BufferModeText));
        Raise(nameof(ErrorText));
        Raise(nameof(ErrorDetailsText));
        Raise(nameof(SizeText));
        Raise(nameof(ProgressText));
        Raise(nameof(SpeedText));
        Raise(nameof(EtaText));
    }

    private void Raise([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = Math.Abs(bytes);
        var unitIndex = 0;
        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        return $"{Math.Max(0, duration.Seconds)}s";
    }
}
