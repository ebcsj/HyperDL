namespace HyperDL.Core;

public enum DownloadSegmentStatus
{
    Pending,
    Downloading,
    Completed,
    Failed,
    Cancelled
}

public sealed class DownloadSegment
{
    public DownloadSegment(
        int index,
        ByteRange range,
        long? currentOffset = null,
        DownloadSegmentStatus status = DownloadSegmentStatus.Pending)
    {
        var initialOffset = currentOffset ?? range.StartOffset;
        if (range.StartOffset < 0 ||
            range.EndOffset < range.StartOffset ||
            initialOffset < range.StartOffset ||
            initialOffset > range.EndOffset + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(currentOffset));
        }

        Index = index;
        StartOffset = range.StartOffset;
        EndOffset = range.EndOffset;
        CurrentOffset = initialOffset;
        BytesDownloaded = initialOffset - range.StartOffset;
        Status = status;
    }

    public int Index { get; }
    public long StartOffset { get; }
    public long EndOffset { get; }
    public long CurrentOffset { get; internal set; }
    public long BytesDownloaded { get; internal set; }
    public long ExpectedLength => EndOffset - StartOffset + 1;
    public DownloadSegmentStatus Status { get; internal set; }
    public string? ErrorMessage { get; internal set; }
}

public sealed record DownloadSegmentProgress(
    int Index,
    long StartOffset,
    long EndOffset,
    long BytesDownloaded,
    long ExpectedLength,
    DownloadSegmentStatus Status,
    string? ErrorMessage);
