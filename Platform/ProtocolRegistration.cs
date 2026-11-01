using System.IO;
using System.Text;
using Microsoft.Win32;

namespace HyperDL.Platform;

/// <summary>
/// Registers the <c>hyperdl://</c> URL scheme under the current user so the
/// Chrome extension can hand downloads to the desktop app. The installer uses
/// the same layout.
/// </summary>
public static class ProtocolRegistration
{
    public const string Scheme = "hyperdl";
    private const string SchemeKeyPath = @"Software\Classes\hyperdl";

    public static void Register()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException("Could not locate HyperDL.exe.");
        }

        using var scheme = Registry.CurrentUser.CreateSubKey(SchemeKeyPath)
            ?? throw new InvalidOperationException("Could not create the protocol registry key.");
        scheme.SetValue(null, "URL:HyperDL Download Protocol");
        scheme.SetValue("URL Protocol", string.Empty);

        using var icon = scheme.CreateSubKey("DefaultIcon")
            ?? throw new InvalidOperationException("Could not create the protocol icon key.");
        icon.SetValue(null, $"\"{executable}\",0");

        using var command = scheme.CreateSubKey(@"shell\open\command")
            ?? throw new InvalidOperationException("Could not create the protocol command key.");
        command.SetValue(null, $"\"{executable}\" \"%1\"");
    }

    public static bool IsRegistered()
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"{SchemeKeyPath}\shell\open\command");
        var value = command?.GetValue(null) as string;
        var executable = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(value) &&
               !string.IsNullOrWhiteSpace(executable) &&
               value.Contains(executable, StringComparison.OrdinalIgnoreCase);
    }

    private const string NativeHostKeyPath =
        @"Software\Google\Chrome\NativeMessagingHosts\com.ebcsj.hyperdl";

    /// <summary>
    /// Registers the native-messaging host for the supplied extension ID and
    /// returns the manifest path. The manifest points Chrome at HyperDL.exe in
    /// stdio mode; repeated calls refresh the manifest (new install path).
    /// </summary>
    public static string RegisterNativeHost(string extensionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            throw new InvalidOperationException("Could not locate HyperDL.exe.");
        }

        var manifestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HyperDL",
            "com.ebcsj.hyperdl.json");
        var manifest =
            $$"""
            {
              "name": "{{NativeHost.HostName}}",
              "description": "HyperDL native messaging host",
              "path": "{{executable.Replace("\\", "\\\\")}}",
              "type": "stdio",
              "allowed_origins": ["chrome-extension://{{extensionId.Trim()}}/"]
            }
            """;
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, manifest, new UTF8Encoding(false));

        using var key = Registry.CurrentUser.CreateSubKey(NativeHostKeyPath)
            ?? throw new InvalidOperationException("Could not create the native host registry key.");
        key.SetValue(null, manifestPath);
        return manifestPath;
    }

    public static void UnregisterNativeHost()
    {
        Registry.CurrentUser.DeleteSubKeyTree(NativeHostKeyPath, false);
    }
}
