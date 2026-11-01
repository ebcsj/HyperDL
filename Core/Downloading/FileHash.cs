using System.IO;
using System.Security.Cryptography;

namespace HyperDL.Core;

/// <summary>
/// Streaming file hashing utilities. Hashes are computed with a bounded
/// buffer so large files never load into memory.
/// </summary>
public static class FileHash
{
    public const int BufferSize = 64 * 1024;

    /// <summary>
    /// Computes the lowercase hexadecimal SHA-256 of a file.
    /// </summary>
    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
