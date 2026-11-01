using HyperDL.Core;

namespace HyperDL.Tests;

public class SegmentSchedulerTests
{
    private static DownloadSegment MakeSegment(int index, long start, long end, long current) =>
        new(index, new ByteRange(start, end), current);

    [Fact]
    public void Constructor_EnqueuesPendingSegmentsAndSkipsFinishedOnes()
    {
        var finished = MakeSegment(1, 0, 9, 10);
        finished.Status = DownloadSegmentStatus.Completed;
        var pending = MakeSegment(2, 10, 19, 15);

        var scheduler = new SegmentScheduler(
            [finished, pending], new object(), dynamicSegmentationEnabled: false, 8, 4);

        Assert.True(scheduler.TryTake(out var taken));
        Assert.Same(pending, taken);
        Assert.Equal(DownloadSegmentStatus.Pending, pending.Status);
        Assert.False(scheduler.TryTake(out _));
    }

    [Fact]
    public void TryTake_SkipsSegmentsThatFinishAfterConstruction()
    {
        var first = MakeSegment(1, 0, 9, 5);
        var second = MakeSegment(2, 10, 19, 10);
        var scheduler = new SegmentScheduler([first, second], new object(), false, 8, 4);

        first.CurrentOffset = 10;
        first.Status = DownloadSegmentStatus.Completed;

        Assert.True(scheduler.TryTake(out var taken));
        Assert.Same(second, taken);
        Assert.False(scheduler.TryTake(out _));
    }

    [Fact]
    public void TrySplit_SplitsLargestDownloadingRangeInHalf()
    {
        var segments = new List<DownloadSegment>
        {
            MakeSegment(1, 0, 15, 16),
            MakeSegment(2, 16, 31, 16),
            MakeSegment(3, 32, 47, 48)
        };
        segments[0].Status = DownloadSegmentStatus.Completed;
        segments[2].Status = DownloadSegmentStatus.Completed;

        var scheduler = new SegmentScheduler(
            segments, new object(), dynamicSegmentationEnabled: true, 8, 4);
        // The constructor normalizes statuses to Pending; mark the in-flight
        // segment as downloading afterwards, like the engine does.
        segments[1].Status = DownloadSegmentStatus.Downloading;

        Assert.True(scheduler.TrySplit(out var created));
        Assert.NotNull(created);
        Assert.Equal(24, created!.StartOffset);
        Assert.Equal(31, created.EndOffset);
        Assert.Equal(23, segments[1].EndOffset);
        Assert.Equal(4, created.Index);
        Assert.Equal(4, scheduler.SegmentCount);
    }

    [Fact]
    public void TrySplit_RespectsMinimumTailSize()
    {
        var segment = MakeSegment(1, 0, 9, 0);
        var scheduler = new SegmentScheduler([segment], new object(), true, 8, 6);
        segment.Status = DownloadSegmentStatus.Downloading;

        Assert.False(scheduler.TrySplit(out _));
    }

    [Fact]
    public void TrySplit_RespectsMaximumSegmentCount()
    {
        var first = MakeSegment(1, 0, 15, 0);
        var second = MakeSegment(2, 16, 31, 16);
        var scheduler = new SegmentScheduler([first, second], new object(), true, 2, 4);
        first.Status = DownloadSegmentStatus.Downloading;
        second.Status = DownloadSegmentStatus.Downloading;

        Assert.False(scheduler.TrySplit(out _));
    }

    [Fact]
    public void TrySplit_ReturnsFalseWhenDisabled()
    {
        var segment = MakeSegment(1, 0, 63, 0);
        var scheduler = new SegmentScheduler([segment], new object(), false, 8, 4);
        segment.Status = DownloadSegmentStatus.Downloading;

        Assert.False(scheduler.TrySplit(out _));
    }

    [Fact]
    public void TrySplit_SkipsRangesCurrentlyBeingWritten()
    {
        var segment = MakeSegment(1, 0, 63, 0);
        var scheduler = new SegmentScheduler([segment], new object(), true, 8, 4);
        segment.Status = DownloadSegmentStatus.Downloading;
        segment.IsWriting = true;

        Assert.False(scheduler.TrySplit(out _));
    }

    [Fact]
    public void Validate_PassesForExactCoverage()
    {
        var first = MakeSegment(1, 0, 9, 10);
        var second = MakeSegment(2, 10, 19, 20);
        foreach (var segment in new[] { first, second })
        {
            segment.Status = DownloadSegmentStatus.Completed;
            segment.BytesDownloaded = segment.CurrentOffset - segment.StartOffset;
        }

        var scheduler = new SegmentScheduler([first, second], new object(), false, 8, 4);

        Assert.True(scheduler.Validate(20, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Validate_FailsOnGap()
    {
        var first = MakeSegment(1, 0, 9, 10);
        var second = MakeSegment(2, 11, 19, 20);
        foreach (var segment in new[] { first, second })
        {
            segment.Status = DownloadSegmentStatus.Completed;
            segment.BytesDownloaded = segment.CurrentOffset - segment.StartOffset;
        }

        var scheduler = new SegmentScheduler([first, second], new object(), false, 8, 4);

        Assert.False(scheduler.Validate(20, out var error));
        Assert.Contains("gap", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FailsOnIncompleteSegment()
    {
        var first = MakeSegment(1, 0, 9, 10);
        first.Status = DownloadSegmentStatus.Completed;
        first.BytesDownloaded = 10;
        var second = MakeSegment(2, 10, 19, 15);
        second.Status = DownloadSegmentStatus.Downloading;

        var scheduler = new SegmentScheduler([first, second], new object(), false, 8, 4);

        Assert.False(scheduler.Validate(20, out _));
    }

    [Fact]
    public void MarkCompleted_ClearsErrorAndSetsStatus()
    {
        var segment = MakeSegment(1, 0, 9, 5);
        segment.Status = DownloadSegmentStatus.Downloading;
        segment.ErrorMessage = "boom";
        var scheduler = new SegmentScheduler([segment], new object(), false, 8, 4);

        Assert.True(scheduler.TryTake(out var taken));
        scheduler.MarkCompleted(taken!);

        Assert.Equal(DownloadSegmentStatus.Completed, taken!.Status);
        Assert.Null(taken.ErrorMessage);
    }
}
