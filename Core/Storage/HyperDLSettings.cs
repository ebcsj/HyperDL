using System.IO;
using System.Text.Json;

namespace HyperDL.Core;

public sealed record HyperDLSettings
{
    public string DownloadDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");
    public int ConnectionCount { get; init; }
    public int MaximumConcurrentDownloads { get; init; } = 2;
    public int GlobalConnectionLimit { get; init; } = DownloadEngine.MaximumSegmentCount;
    public bool LowRamMode { get; init; }
    public bool LegalUseAcknowledged { get; init; }
}

public static class UserSettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperDL",
        "settings.json");

    public static HyperDLSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new HyperDLSettings();
        }

        var settings = JsonSerializer.Deserialize<HyperDLSettings>(
            File.ReadAllText(SettingsPath));
        if (settings is null ||
            string.IsNullOrWhiteSpace(settings.DownloadDirectory) ||
            settings.ConnectionCount is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64) ||
            settings.MaximumConcurrentDownloads is < 1 or > 64 ||
            settings.GlobalConnectionLimit is < 1 or > DownloadEngine.MaximumSegmentCount)
        {
            throw new InvalidDataException("HyperDL settings file contains invalid values.");
        }

        return settings;
    }

    public static async Task SaveAsync(
        HyperDLSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
