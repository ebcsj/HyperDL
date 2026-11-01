using System.IO;

namespace HyperDL.Core;

public enum DiskSpaceStatus
{
    Sufficient,
    InsufficientSpace,
    FileSystemLimit
}

public sealed record DiskSpaceCheckResult(
    DiskSpaceStatus Status,
    long RequiredBytes,
    long AvailableBytes,
    string? FileSystemFormat)
{
    public long ShortfallBytes =>
        Status == DiskSpaceStatus.InsufficientSpace
            ? Math.Max(0, RequiredBytes - AvailableBytes)
            : 0;
}

/// <summary>
/// Checks free space and file-system limits before a transfer starts. The
/// checks are deliberately conservative but never block a download when the
/// volume information cannot be read (for example on an unusual network share).
/// </summary>
public static class DiskSpaceGuard
{
    public const long FreeSpaceReserveBytes = 64L * 1024 * 1024;
    public const long Fat32MaximumFileSize = 4L * 1024 * 1024 * 1024 - 1;

    public static DiskSpaceCheckResult Check(string directory, long requiredBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (requiredBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredBytes));
        }

        try
        {
            var existing = FindExistingDirectory(directory);
            if (existing is null)
            {
                return Sufficient(requiredBytes);
            }

            var root = Path.GetPathRoot(existing);
            if (string.IsNullOrWhiteSpace(root))
            {
                return Sufficient(requiredBytes);
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                return Sufficient(requiredBytes);
            }

            var format = drive.DriveFormat;
            return Evaluate(requiredBytes, drive.AvailableFreeSpace, format);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ArgumentException or NotSupportedException)
        {
            return Sufficient(requiredBytes);
        }
    }

    /// <summary>
    /// Pure decision function, exposed so tests can cover full-disk and FAT32
    /// behaviour without filling a real volume.
    /// </summary>
    public static DiskSpaceCheckResult Evaluate(
        long requiredBytes,
        long availableBytes,
        string? fileSystemFormat)
    {
        if (requiredBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredBytes));
        }

        if (availableBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(availableBytes));
        }

        if (IsFat32(fileSystemFormat) && requiredBytes > Fat32MaximumFileSize)
        {
            return new DiskSpaceCheckResult(
                DiskSpaceStatus.FileSystemLimit,
                requiredBytes,
                availableBytes,
                fileSystemFormat);
        }

        var reserve = requiredBytes < FreeSpaceReserveBytes
            ? FreeSpaceReserveBytes / 4
            : FreeSpaceReserveBytes;
        var needed = requiredBytes > long.MaxValue - reserve
            ? long.MaxValue
            : requiredBytes + reserve;
        return new DiskSpaceCheckResult(
            availableBytes >= needed
                ? DiskSpaceStatus.Sufficient
                : DiskSpaceStatus.InsufficientSpace,
            requiredBytes,
            availableBytes,
            fileSystemFormat);
    }

    public static void EnsureAvailable(string directory, long requiredBytes)
    {
        var result = Check(directory, requiredBytes);
        switch (result.Status)
        {
            case DiskSpaceStatus.InsufficientSpace:
                throw new InsufficientDiskSpaceException(
                    $"Not enough free disk space. Need {result.RequiredBytes} bytes, " +
                    $"available {result.AvailableBytes} bytes.",
                    result.RequiredBytes,
                    result.AvailableBytes);
            case DiskSpaceStatus.FileSystemLimit:
                throw new FileSystemLimitException(
                    $"The destination volume uses {result.FileSystemFormat ?? "FAT32"}, " +
                    $"which cannot store files larger than {Fat32MaximumFileSize} bytes.",
                    result.FileSystemFormat ?? "FAT32");
        }
    }

    private static DiskSpaceCheckResult Sufficient(long requiredBytes) =>
        new(DiskSpaceStatus.Sufficient, requiredBytes, long.MaxValue, null);

    private static bool IsFat32(string? format) =>
        format?.Trim().Equals("FAT32", StringComparison.OrdinalIgnoreCase) == true;

    private static string? FindExistingDirectory(string directory)
    {
        var current = Path.GetFullPath(directory);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (Directory.Exists(current))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return null;
    }
}

/// <summary>
/// Raised when the volume does not have enough free space for the transfer.
/// </summary>
public sealed class InsufficientDiskSpaceException : IOException
{
    public InsufficientDiskSpaceException(
        string message,
        long requiredBytes,
        long availableBytes)
        : base(message)
    {
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
        HResult = unchecked((int)0x80070070);
    }

    public long RequiredBytes { get; }
    public long AvailableBytes { get; }
}

/// <summary>
/// Raised when the destination file system (typically FAT32) cannot store the
/// requested file size.
/// </summary>
public sealed class FileSystemLimitException : IOException
{
    public FileSystemLimitException(string message, string fileSystemFormat)
        : base(message)
    {
        FileSystemFormat = fileSystemFormat;
    }

    public string FileSystemFormat { get; }
}
