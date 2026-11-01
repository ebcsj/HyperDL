using System.IO;
using HyperDL.Core;

namespace HyperDL.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _directory;

    public SettingsStoreTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"HyperDL.Settings-{Guid.NewGuid():N}");
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
    public async Task SaveToThenLoadFrom_RoundTripsAllFields()
    {
        var path = Path.Combine(_directory, "settings.json");
        var settings = new HyperDLSettings
        {
            DownloadDirectory = _directory,
            ConnectionCount = 8,
            MaximumConcurrentDownloads = 3,
            BufferMode = BufferMode.Fast,
            SpeedLimitKBps = 500,
            Http2Enabled = true,
            DarkMode = true,
            NativeMessagingEnabled = true,
            ExtensionId = "abcdefghijklmnopabcdefghijklmnop",
            AutoUpdateDownload = false,
            Language = AppLanguage.TraditionalChinese
        };

        await UserSettingsStore.SaveToAsync(settings, path);
        var loaded = UserSettingsStore.LoadFrom(path);

        Assert.Equal(settings.DownloadDirectory, loaded.DownloadDirectory);
        Assert.Equal(8, loaded.ConnectionCount);
        Assert.Equal(3, loaded.MaximumConcurrentDownloads);
        Assert.Equal(BufferMode.Fast, loaded.BufferMode);
        Assert.Equal(500, loaded.SpeedLimitKBps);
        Assert.True(loaded.Http2Enabled);
        Assert.True(loaded.DarkMode);
        Assert.True(loaded.NativeMessagingEnabled);
        Assert.Equal(settings.ExtensionId, loaded.ExtensionId);
        Assert.False(loaded.AutoUpdateDownload);
        Assert.Equal(AppLanguage.TraditionalChinese, loaded.Language);
    }

    [Fact]
    public async Task SaveToAsync_IsAtomicAndLeavesNoTempFile()
    {
        var path = Path.Combine(_directory, "settings.json");

        await UserSettingsStore.SaveToAsync(new HyperDLSettings { DownloadDirectory = _directory }, path);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void LoadFrom_MissingFileReturnsDefaults()
    {
        var loaded = UserSettingsStore.LoadFrom(Path.Combine(_directory, "missing.json"));

        Assert.True(loaded.CheckForUpdates);
        Assert.False(loaded.NativeMessagingEnabled);
        Assert.False(loaded.Http2Enabled);
    }

    [Fact]
    public void LoadFrom_RejectsInvalidSettings()
    {
        var path = Path.Combine(_directory, "bad.json");
        File.WriteAllText(path, """{"DownloadDirectory":"", "ConnectionCount":3}""");

        Assert.Throws<InvalidDataException>(() => UserSettingsStore.LoadFrom(path));
    }
}
