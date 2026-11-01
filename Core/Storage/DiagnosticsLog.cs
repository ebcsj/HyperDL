using System.IO;
using System.Text;

namespace HyperDL.Core;

/// <summary>
/// Minimal append-only diagnostics log. Logging must never break a download or
/// leak credentials, so callers pass short operational messages and exceptions
/// are reduced to their type and message.
/// </summary>
public static class DiagnosticsLog
{
    private static readonly object Sync = new();

    public static string DirectoryPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HyperDL",
        "logs");

    public static string CurrentFilePath => Path.Combine(
        DirectoryPath,
        $"hyperdl-{DateTime.Now:yyyyMMdd}.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        var detail = exception is null
            ? message
            : $"{message} :: {exception.GetType().Name}: {exception.Message}";
        Write("ERROR", detail);
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(CurrentFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Diagnostics are best effort. A read-only profile must not fail a download.
        }
    }
}
