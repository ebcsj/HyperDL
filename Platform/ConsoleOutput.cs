using System.Runtime.InteropServices;

namespace HyperDL.Platform;

/// <summary>
/// Attaches the detached WPF process to the parent console so CLI commands
/// (--list, --pause-all, …) can print their JSON output.
/// </summary>
internal static class ConsoleOutput
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    public static void Attach()
    {
        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch
        {
            // Console output is best effort.
        }
    }

    public static void WriteLine(string message)
    {
        Console.WriteLine(message);
        Console.Out.Flush();
    }

    public static void Detach()
    {
        try
        {
            FreeConsole();
        }
        catch
        {
        }
    }
}
