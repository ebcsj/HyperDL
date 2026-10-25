namespace HyperDL.Core;

public static class RangeCalculator
{
    public static IReadOnlyList<ByteRange> Calculate(long fileSize, int requestedRangeCount)
    {
        if (fileSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize), "File size must be positive.");
        }

        if (requestedRangeCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRangeCount),
                "Range count must be positive.");
        }

        var rangeCount = (int)Math.Min(fileSize, requestedRangeCount);
        var baseRangeSize = fileSize / rangeCount;
        var extraBytes = fileSize % rangeCount;
        var ranges = new ByteRange[rangeCount];
        long startOffset = 0;

        for (var index = 0; index < rangeCount; index++)
        {
            var rangeSize = baseRangeSize + (index < extraBytes ? 1 : 0);
            var endOffset = startOffset + rangeSize - 1;
            ranges[index] = new ByteRange(startOffset, endOffset);
            startOffset = endOffset + 1;
        }

        return ranges;
    }
}
