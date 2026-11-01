namespace HyperDL.Core;

/// <summary>
/// Owns the mutable segment map for one download. Workers take ranges from the
/// queue; when the queue is empty an idle worker may split a large in-flight
/// range so a slow connection cannot become the critical path.
/// </summary>
internal sealed class SegmentScheduler
{
    private readonly object _sync;
    private readonly List<DownloadSegment> _segments;
    private readonly Queue<DownloadSegment> _ready = new();
    private readonly bool _dynamicSegmentationEnabled;
    private readonly int _maximumSegmentCount;
    private readonly long _minimumSegmentSize;
    private int _nextIndex;

    public SegmentScheduler(
        IReadOnlyList<DownloadSegment> segments,
        object syncRoot,
        bool dynamicSegmentationEnabled,
        int maximumSegmentCount,
        long minimumSegmentSize)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(syncRoot);

        _sync = syncRoot;
        _segments = [.. segments];
        _dynamicSegmentationEnabled = dynamicSegmentationEnabled;
        _maximumSegmentCount = Math.Max(_segments.Count, maximumSegmentCount);
        _minimumSegmentSize = Math.Max(1, minimumSegmentSize);
        _nextIndex = _segments.Count == 0 ? 1 : _segments.Max(segment => segment.Index) + 1;

        foreach (var segment in _segments.OrderBy(segment => segment.StartOffset))
        {
            if (segment.Status == DownloadSegmentStatus.Completed &&
                segment.CurrentOffset > segment.EndOffset)
            {
                continue;
            }

            if (segment.CurrentOffset > segment.EndOffset)
            {
                segment.Status = DownloadSegmentStatus.Completed;
                continue;
            }

            segment.Status = DownloadSegmentStatus.Pending;
            segment.ErrorMessage = null;
            _ready.Enqueue(segment);
        }
    }

    public IReadOnlyList<DownloadSegment> Snapshot()
    {
        lock (_sync)
        {
            return _segments.ToArray();
        }
    }

    public bool TryTake(out DownloadSegment? segment)
    {
        lock (_sync)
        {
            while (_ready.Count > 0)
            {
                var candidate = _ready.Dequeue();
                if (candidate.Status == DownloadSegmentStatus.Completed ||
                    candidate.CurrentOffset > candidate.EndOffset)
                {
                    if (candidate.CurrentOffset > candidate.EndOffset)
                    {
                        candidate.Status = DownloadSegmentStatus.Completed;
                    }

                    continue;
                }

                segment = candidate;
                return true;
            }
        }

        segment = null;
        return false;
    }

    /// <summary>
    /// Splits the largest active range in half. The caller is an idle worker,
    /// so the newly created range is returned for immediate download.
    /// </summary>
    public bool TrySplit(out DownloadSegment? newSegment)
    {
        lock (_sync)
        {
            newSegment = null;
            if (!_dynamicSegmentationEnabled || _segments.Count >= _maximumSegmentCount)
            {
                return false;
            }

            DownloadSegment? best = null;
            long bestRemaining = 0;
            foreach (var segment in _segments)
            {
                if (segment.Status != DownloadSegmentStatus.Downloading || segment.IsWriting)
                {
                    continue;
                }

                var remaining = segment.EndOffset - segment.CurrentOffset + 1;
                if (remaining < _minimumSegmentSize * 2 || remaining <= bestRemaining)
                {
                    continue;
                }

                best = segment;
                bestRemaining = remaining;
            }

            if (best is null)
            {
                return false;
            }

            var splitOffset = best.CurrentOffset + bestRemaining / 2;
            if (splitOffset <= best.CurrentOffset || splitOffset > best.EndOffset)
            {
                return false;
            }

            var newEndOffset = best.EndOffset;
            best.EndOffset = splitOffset - 1;
            newSegment = new DownloadSegment(
                _nextIndex++,
                new ByteRange(splitOffset, newEndOffset));
            newSegment.Status = DownloadSegmentStatus.Pending;
            _segments.Add(newSegment);

            // The calling worker takes ownership of the new range directly; it is
            // deliberately not enqueued so two workers cannot claim it.
            return true;
        }
    }

    public void MarkCompleted(DownloadSegment segment)
    {
        lock (_sync)
        {
            segment.Status = DownloadSegmentStatus.Completed;
            segment.ErrorMessage = null;
        }
    }

    public bool Validate(long totalBytes, out string? error)
    {
        lock (_sync)
        {
            var ordered = _segments.OrderBy(segment => segment.StartOffset).ToArray();
            if (ordered.Length == 0)
            {
                error = "No segments were scheduled.";
                return false;
            }

            if (ordered[0].StartOffset != 0 ||
                ordered[^1].EndOffset != totalBytes - 1)
            {
                error = "Segments do not cover the complete file.";
                return false;
            }

            long expectedStart = 0;
            long downloaded = 0;
            foreach (var segment in ordered)
            {
                if (segment.StartOffset != expectedStart ||
                    segment.EndOffset < segment.StartOffset)
                {
                    error = "Segments overlap or contain a gap.";
                    return false;
                }

                if (segment.Status != DownloadSegmentStatus.Completed ||
                    segment.CurrentOffset != segment.EndOffset + 1 ||
                    segment.BytesDownloaded != segment.ExpectedLength)
                {
                    error = $"Segment {segment.Index} did not complete.";
                    return false;
                }

                expectedStart = segment.EndOffset + 1;
                downloaded += segment.BytesDownloaded;
            }

            if (expectedStart != totalBytes || downloaded != totalBytes)
            {
                error = "Downloaded byte count does not match the file size.";
                return false;
            }

            error = null;
            return true;
        }
    }

    public int IncompleteCount
    {
        get
        {
            lock (_sync)
            {
                return _segments.Count(segment =>
                    segment.Status != DownloadSegmentStatus.Completed);
            }
        }
    }

    public int SegmentCount
    {
        get
        {
            lock (_sync)
            {
                return _segments.Count;
            }
        }
    }
}
