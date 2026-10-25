using HyperDL.Core;

namespace HyperDL.Tests;

public class RangeCalculatorTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 4)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(3, 4)]
    [InlineData(10, 1)]
    [InlineData(10, 2)]
    [InlineData(10, 4)]
    [InlineData(1001, 1)]
    [InlineData(1001, 2)]
    [InlineData(1001, 4)]
    [InlineData(81920, 1)]
    [InlineData(81920, 2)]
    [InlineData(81920, 4)]
    [InlineData(81921, 1)]
    [InlineData(81921, 2)]
    [InlineData(81921, 4)]
    public void Calculate_CoversFileExactlyWithoutGapsOrOverlap(long fileSize, int requestedCount)
    {
        var ranges = RangeCalculator.Calculate(fileSize, requestedCount);

        Assert.Equal((int)Math.Min(fileSize, requestedCount), ranges.Count);
        Assert.Equal(0, ranges[0].StartOffset);
        Assert.Equal(fileSize - 1, ranges[^1].EndOffset);

        long totalCoveredBytes = 0;
        for (var index = 0; index < ranges.Count; index++)
        {
            var range = ranges[index];
            Assert.True(range.StartOffset >= 0);
            Assert.True(range.StartOffset <= range.EndOffset);
            Assert.True(range.EndOffset < fileSize);
            totalCoveredBytes += range.EndOffset - range.StartOffset + 1;

            if (index > 0)
            {
                Assert.Equal(ranges[index - 1].EndOffset + 1, range.StartOffset);
            }
        }

        Assert.Equal(fileSize, totalCoveredBytes);
    }

    [Fact]
    public void Calculate_DistributesRemainderAcrossLeadingRanges()
    {
        var ranges = RangeCalculator.Calculate(fileSize: 1001, requestedRangeCount: 4);

        Assert.Equal(
            new long[] { 251, 250, 250, 250 },
            ranges.Select(range => range.EndOffset - range.StartOffset + 1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    public void Calculate_RejectsNonPositiveInputs(long fileSize, int requestedCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RangeCalculator.Calculate(fileSize, requestedCount));
    }
}
