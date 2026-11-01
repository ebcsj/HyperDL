using System.IO;
using HyperDL.Core;

namespace HyperDL.Tests;

public class DiskSpaceGuardTests
{
    [Fact]
    public void Evaluate_Fat32RejectsFilesOverTheLimit()
    {
        var result = DiskSpaceGuard.Evaluate(
            5L * 1024 * 1024 * 1024,
            10L * 1024 * 1024 * 1024,
            "FAT32");

        Assert.Equal(DiskSpaceStatus.FileSystemLimit, result.Status);
        Assert.Equal("FAT32", result.FileSystemFormat);
    }

    [Fact]
    public void Evaluate_Fat32AllowsFilesUnderTheLimit()
    {
        var result = DiskSpaceGuard.Evaluate(
            DiskSpaceGuard.Fat32MaximumFileSize,
            10L * 1024 * 1024 * 1024,
            "fat32");

        Assert.Equal(DiskSpaceStatus.Sufficient, result.Status);
    }

    [Fact]
    public void Evaluate_ReportsInsufficientSpaceWithShortfall()
    {
        var required = 100L * 1024 * 1024 * 1024;
        var available = 1L * 1024 * 1024 * 1024;

        var result = DiskSpaceGuard.Evaluate(required, available, "NTFS");

        Assert.Equal(DiskSpaceStatus.InsufficientSpace, result.Status);
        Assert.Equal(required - available, result.ShortfallBytes);
    }

    [Fact]
    public void Evaluate_AppliesReducedReserveForSmallTransfers()
    {
        var required = 1L * 1024 * 1024;
        var reserve = DiskSpaceGuard.FreeSpaceReserveBytes / 4;

        var enough = DiskSpaceGuard.Evaluate(required, required + reserve, "NTFS");
        var shortByOne = DiskSpaceGuard.Evaluate(required, required + reserve - 1, "NTFS");

        Assert.Equal(DiskSpaceStatus.Sufficient, enough.Status);
        Assert.Equal(DiskSpaceStatus.InsufficientSpace, shortByOne.Status);
    }

    [Fact]
    public void Evaluate_AppliesFullReserveForLargeTransfers()
    {
        var required = 1L * 1024 * 1024 * 1024;
        var reserve = DiskSpaceGuard.FreeSpaceReserveBytes;

        var enough = DiskSpaceGuard.Evaluate(required, required + reserve, "NTFS");
        var shortByOne = DiskSpaceGuard.Evaluate(required, required + reserve - 1, "NTFS");

        Assert.Equal(DiskSpaceStatus.Sufficient, enough.Status);
        Assert.Equal(DiskSpaceStatus.InsufficientSpace, shortByOne.Status);
    }

    [Fact]
    public void Evaluate_NonFat32FormatsHaveNoFileSizeCeiling()
    {
        var result = DiskSpaceGuard.Evaluate(
            100L * 1024 * 1024 * 1024,
            200L * 1024 * 1024 * 1024,
            "NTFS");

        Assert.Equal(DiskSpaceStatus.Sufficient, result.Status);
    }

    [Fact]
    public void EnsureAvailable_ThrowsForRequirementBeyondAnyVolume()
    {
        using var temp = new TempDirectory();

        Assert.Throws<InsufficientDiskSpaceException>(
            () => DiskSpaceGuard.EnsureAvailable(temp.Path, long.MaxValue / 2));
    }

    [Theory]
    [InlineData(-1, 100, "NTFS")]
    [InlineData(100, -1, "NTFS")]
    public void Evaluate_RejectsNegativeInputs(long required, long available, string format)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DiskSpaceGuard.Evaluate(required, available, format));
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"HyperDL.DiskGuard-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
