using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HyperDL.Core;

public enum AppLanguage
{
    System,
    English,
    TraditionalChinese
}

public sealed record HyperDLSettings
{
    public string DownloadDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads");
    public int ConnectionCount { get; init; }
    public int MaximumConcurrentDownloads { get; init; } = 2;
    public int GlobalConnectionLimit { get; init; } = DownloadEngine.MaximumSegmentCount;
    public BufferMode BufferMode { get; init; } = BufferMode.Auto;
    public bool DynamicSegmentation { get; init; } = true;
    public bool AutoResume { get; init; } = true;
    public AppLanguage Language { get; init; } = AppLanguage.System;
    public int SpeedLimitKBps { get; init; }
    public int MaxConnectionsPerHost { get; init; }
    public bool ConfirmExternalDownloads { get; init; } = true;
    public bool ClipboardMonitoring { get; init; }
    public bool LegalUseAcknowledged { get; init; }
    public double WindowWidth { get; init; } = 1080;
    public double WindowHeight { get; init; } = 760;
    public double? WindowLeft { get; init; }
    public double? WindowTop { get; init; }
    public Dictionary<string, double> ColumnWidths { get; init; } = new();

    /// <summary>
    /// Legacy setting retained so older settings.json files migrate cleanly.
    /// </summary>
    public bool LowRamMode { get; init; }

    public DownloadEngineOptions ToEngineOptions() => new()
    {
        BufferMode = BufferMode,
        DynamicSegmentation = DynamicSegmentation,
        MaximumConnectionsPerHost = MaxConnectionsPerHost,
        SpeedLimitKBps = SpeedLimitKBps
    };
}

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperDL",
        "settings.json");

    public static string SettingsFilePath => SettingsPath;

    public static HyperDLSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new HyperDLSettings();
        }

        var json = File.ReadAllText(SettingsPath);
        using var document = JsonDocument.Parse(json);
        var hasBufferMode = document.RootElement.TryGetProperty(nameof(HyperDLSettings.BufferMode), out _);
        var settings = JsonSerializer.Deserialize<HyperDLSettings>(json, JsonOptions);
        if (settings is null)
        {
            throw new InvalidDataException("HyperDL settings file is empty or invalid.");
        }

        if (!hasBufferMode && settings.LowRamMode)
        {
            settings = settings with { BufferMode = BufferMode.LowRam };
        }

        Validate(settings);
        return settings;
    }

    public static void Validate(HyperDLSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(settings.DownloadDirectory) ||
            settings.ConnectionCount is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64) ||
            settings.MaximumConcurrentDownloads is < 1 or > 64 ||
            settings.GlobalConnectionLimit is < 1 or > DownloadEngine.MaximumSegmentCount ||
            settings.MaxConnectionsPerHost is < 0 or > DownloadEngine.MaximumSegmentCount ||
            settings.SpeedLimitKBps is < 0 or > 1_000_000 ||
            !Enum.IsDefined(settings.BufferMode) ||
            !Enum.IsDefined(settings.Language) ||
            settings.ColumnWidths is null ||
            settings.ColumnWidths.Any(pair =>
                string.IsNullOrWhiteSpace(pair.Key) ||
                !double.IsFinite(pair.Value) ||
                pair.Value < 0 || pair.Value > 10_000) ||
            settings.WindowWidth < 640 ||
            settings.WindowHeight < 480)
        {
            throw new InvalidDataException("HyperDL settings file contains invalid values.");
        }
    }

    public static async Task SaveAsync(
        HyperDLSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporaryPath = SettingsPath + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
    }
}
