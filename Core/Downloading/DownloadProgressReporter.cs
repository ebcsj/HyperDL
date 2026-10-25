using System.Diagnostics;

namespace HyperDL.Core;

internal sealed class DownloadProgressReporter
{
    private readonly object _sync;
    private readonly IReadOnlyList<DownloadSegment> _segments;
    private readonly IProgress<DownloadProgress>? _progress;
    private readonly string _fileName;
    private readonly long _totalBytes;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private TimeSpan _lastReportTime;
    private long _lastReportedBytes;
    private long _totalDownloaded;

    public DownloadProgressReporter(
        IReadOnlyList<DownloadSegment> segments,
        IProgress<DownloadProgress>? progress,
        string fileName,
        long totalBytes,
        object? syncRoot = null)
    {
        _sync = syncRoot ?? new object();
        _segments = segments;
        _progress = progress;
        _fileName = fileName;
        _totalBytes = totalBytes;
        _totalDownloaded = segments.Sum(segment => segment.BytesDownloaded);
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
            var speed = elapsed.TotalSeconds > 0
                ? (totalDownloaded - _lastReportedBytes) / elapsed.TotalSeconds
                : 0;
            var segmentProgress = _segments
                .Select(segment => new DownloadSegmentProgress(
                    segment.Index,
                    segment.StartOffset,
                    segment.EndOffset,
                    segment.BytesDownloaded,
                    segment.ExpectedLength,
                    segment.Status,
                    segment.ErrorMessage))
                .ToArray();

            _progress?.Report(new DownloadProgress(
                _fileName,
                totalDownloaded,
                _totalBytes,
                speed,
                totalDownloaded * 100d / _totalBytes,
                _segments.Count,
                segmentProgress,
                _segments.Count(segment => segment.Status == DownloadSegmentStatus.Downloading)));

            _lastReportTime = now;
            _lastReportedBytes = totalDownloaded;
        }
    }

    public void RecordWritten(DownloadSegment segment, int bytesWritten)
    {
        lock (_sync)
        {
            segment.BytesDownloaded += bytesWritten;
            segment.CurrentOffset += bytesWritten;
            _totalDownloaded += bytesWritten;
        }
    }

    public void SetStatus(DownloadSegment segment, DownloadSegmentStatus status, string? errorMessage = null)
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
