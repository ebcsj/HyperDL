using System.IO;
using HyperDL.Core;

namespace HyperDL.Tests;

public class FileHashTests : IDisposable
{
    private readonly string _directory;

    public FileHashTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"HyperDL.FileHash-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ComputeSha256Async_KnownVector()
    {
        var path = Path.Combine(_directory, "abc.txt");
        await File.WriteAllTextAsync(path, "abc");

        var hash = await FileHash.ComputeSha256Async(path);

        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            hash);
    }

    [Fact]
    public async Task ComputeSha256Async_EmptyFile()
    {
        var path = Path.Combine(_directory, "empty.bin");
        await File.WriteAllBytesAsync(path, Array.Empty<byte>());

        var hash = await FileHash.ComputeSha256Async(path);

        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            hash);
    }

    [Fact]
    public async Task ComputeSha256Async_LargerFileIsDeterministic()
    {
        var path = Path.Combine(_directory, "large.bin");
        var bytes = new byte[512 * 1024];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index * 7);
        }

        await File.WriteAllBytesAsync(path, bytes);
        var first = await FileHash.ComputeSha256Async(path);
        var second = await FileHash.ComputeSha256Async(path);

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public async Task ComputeSha256Async_MissingFileThrows()
    {
        var path = Path.Combine(_directory, "missing.bin");

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => FileHash.ComputeSha256Async(path));
    }
}
