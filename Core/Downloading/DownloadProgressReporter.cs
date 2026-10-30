using System.Diagnostics;

namespace HyperDL.Core;

internal sealed class DownloadProgressReporter
{
    private readonly object _sync;
    private readonly SegmentScheduler _scheduler;
    private readonly IProgress<DownloadProgress>? _progress;
    private readonly string _fileName;
    private readonly long _totalBytes;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastReportTime;
    private long _lastReportedBytes;
    private long _totalDownloaded;
    private double _smoothedBytesPerSecond;

    public DownloadProgressReporter(
        SegmentScheduler scheduler,
        IProgress<DownloadProgress>? progress,
        string fileName,
        long totalBytes,
        object syncRoot)
    {
        _sync = syncRoot;
        _scheduler = scheduler;
        _progress = progress;
        _fileName = fileName;
        _totalBytes = totalBytes;
        lock (_sync)
        {
            _totalDownloaded = scheduler.Snapshot().Sum(segment => segment.BytesDownloaded);
        }

        _lastReportedBytes = _totalDownloaded;
    }

    public void Report(bool force = false)
    {
        lock (_sync)
        {
            var now = _stopwatch.Elapsed;
            var elapsed = now - _lastReportTime;
            if (!force && elapsed < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            var totalDownloaded = _totalDownloaded;
            var rawSpeed = elapsed.TotalSeconds > 0
                ? (totalDownloaded - _lastReportedBytes) / elapsed.TotalSeconds
                : 0;
            _smoothedBytesPerSecond = _smoothedBytesPerSecond <= 0
                ? rawSpeed
                : _smoothedBytesPerSecond * 0.7 + rawSpeed * 0.3;
            var segments = _scheduler.Snapshot();
            var segmentProgress = segments
                .Select(segment => new DownloadSegmentProgress(
                    segment.Index,
                    segment.StartOffset,
                    segment.EndOffset,
                    segment.BytesDownloaded,
                    segment.ExpectedLength,
                    segment.Status,
                    segment.ErrorMessage))
                .ToArray();

            _lastReportTime = now;
            _lastReportedBytes = totalDownloaded;

            try
            {
                _progress?.Report(new DownloadProgress(
                    _fileName,
                    totalDownloaded,
                    _totalBytes,
                    _smoothedBytesPerSecond,
                    totalDownloaded * 100d / _totalBytes,
                    segments.Count,
                    segmentProgress,
                    segments.Count(segment => segment.Status == DownloadSegmentStatus.Downloading)));
            }
            catch
            {
                // A poorly behaved progress consumer must never fail the download.
            }
        }
    }

    public void RecordWritten(DownloadSegment segment, int bytesWritten)
    {
        lock (_sync)
        {
            var writable = (int)Math.Clamp(
                segment.EndOffset - segment.CurrentOffset + 1,
                0,
                bytesWritten);
            if (writable <= 0)
            {
                return;
            }

            segment.BytesDownloaded += writable;
            segment.CurrentOffset += writable;
            _totalDownloaded += writable;
        }
    }

    public void SetStatus(
        DownloadSegment segment,
        DownloadSegmentStatus status,
        string? errorMessage = null)
    {
        lock (_sync)
        {
            segment.Status = status;
            segment.ErrorMessage = errorMessage;
        }

        Report(force: true);
    }

    public long TotalDownloaded
    {
        get
        {
            lock (_sync)
            {
                return _totalDownloaded;
            }
        }
    }
}
