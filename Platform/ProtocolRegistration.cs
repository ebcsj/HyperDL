using System.IO;
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
}
