using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using HyperDL.Core;
using HyperDL.Localization;
using HyperDL.Platform;

namespace HyperDL;

public partial class App : Application
{
    private const string MutexName = "HyperDL.SingleInstance.v2";
    private const string PipeName = "HyperDL.Ipc.v2";
    private const string ActivateMessage = "__hyperdl_activate__";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _ipcCancellation;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Native-messaging host mode: Chrome launched HyperDL.exe over stdio.
        // No window, no mutex — the process lives as long as the pipe does.
        if (e.Args.Any(arg => arg.Equals("--native-host", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                NativeHost.RunAsync(
                    Console.OpenStandardInput(),
                    Console.OpenStandardOutput(),
                    DispatchNativeMessageAsync).GetAwaiter().GetResult();
            }
            catch
            {
                // Chrome closed the pipe; the host exits quietly.
            }

            Shutdown();
            return;
        }

        var settings = LoadSettings(out var settingsError);
        LocalizationManager.Instance.Apply(settings.Language);
        ApplyTheme(settings.DarkMode);

        var payload = string.Join(' ', e.Args);

        // CLI command mode (--list, --pause-all, …): talk to the running
        // instance over the named pipe and print the JSON response.
        var cli = ParseCliCommand(e.Args);
        if (cli.Command is not null)
        {
            ConsoleOutput.Attach();
            var response = TrySendIpcCommand(cli);
            if (response is { } text)
            {
                ConsoleOutput.WriteLine(text);
                ConsoleOutput.Detach();
                Shutdown();
                return;
            }

            // No running instance: importing URLs falls back to a normal
            // cold-start activation; pure control commands report offline.
            if (cli.Command == "import" && cli.Urls.Count > 0)
            {
                payload = string.Join(' ', cli.Urls);
            }
            else
            {
                ConsoleOutput.WriteLine(IpcProtocol.BuildResponse(
                    new IpcResponse(false, "HyperDL is not running.", null, null)));
                ConsoleOutput.Detach();
                Shutdown();
                return;
            }
        }

        var parsed = ActivationParser.Parse(payload);

        var createdNew = false;
        _mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            _ = ForwardToRunningInstanceAsync(
                string.IsNullOrWhiteSpace(payload) ? ActivateMessage : payload);
            Shutdown();
            return;
        }

        _ipcCancellation = new CancellationTokenSource();
        _ = RunPipeServerAsync(_ipcCancellation.Token);

        try
        {
            _window = new MainWindow(
                settings,
                parsed,
                settingsError);
            MainWindow = _window;
            _window.Show();
            if (!string.IsNullOrWhiteSpace(parsed.InvalidText))
            {
                MessageBox.Show(
                    _window,
                    LocalizationManager.GetString("InvalidUrlMessage"),
                    LocalizationManager.GetString("InvalidUrlTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                string.Format(
                    LocalizationManager.GetString("DatabaseErrorFormat"),
                    exception.Message),
                LocalizationManager.GetString("AppTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _ipcCancellation?.Cancel();
        _ipcCancellation?.Dispose();
        if (_ownsMutex)
        {
            try
            {
                _mutex?.ReleaseMutex();
            }
            catch
            {
                // The mutex may already have been released during an abnormal exit.
            }
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }

    private sealed record CliCommand(string Command, IReadOnlyList<string> Urls);

    private static CliCommand ParseCliCommand(string[] args)
    {
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index].Trim();
            switch (arg.ToLowerInvariant())
            {
                case "--list":
                    return new CliCommand("list", []);
                case "--pause-all":
                    return new CliCommand("pause-all", []);
                case "--resume-all":
                    return new CliCommand("resume-all", []);
                case "--clear-completed":
                    return new CliCommand("clear-completed", []);
                case "--import" when index + 1 < args.Length:
                {
                    var urls = new List<string>();
                    try
                    {
                        foreach (var line in File.ReadAllLines(args[index + 1]))
                        {
                            var token = line.Trim().Trim('"', '\'', '<', '>');
                            if (Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
                                DownloadUrlValidator.IsSupported(uri))
                            {
                                urls.Add(token);
                            }
                        }
                    }
                    catch
                    {
                        // A missing file yields an empty import.
                    }

                    return new CliCommand("import", urls);
                }
            }
        }

        return new CliCommand("", []);
    }

    /// <summary>
    /// Sends a CLI request to the running instance. Returns null when no
    /// instance is listening (cold start should take over instead).
    /// </summary>
    private static string? TrySendIpcCommand(CliCommand command)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            client.Connect(1000);
            var request = IpcProtocol.BuildRequest(command.Command, command.Urls);
            var bytes = Encoding.UTF8.GetBytes(request + "\n");
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            using var reader = new StreamReader(client, Encoding.UTF8);
            return reader.ReadLine();
        }
        catch
        {
            return null;
        }
    }

    private async Task RunPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    await using var writer = new StreamWriter(
                        server,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                    {
                        AutoFlush = true
                    };

                    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null)
                    {
                        continue;
                    }

                    var response = await Dispatcher.InvokeAsync<Task<string>>(async () =>
                    {
                        if (_window is null)
                        {
                            return IpcProtocol.BuildResponse(
                                new IpcResponse(false, "HyperDL is closing.", null, null));
                        }

                        var trimmed = line.Trim();
                        if (!IpcProtocol.LooksLikeRequest(trimmed))
                        {
                            // Legacy client: a raw activation payload.
                            var parsed = ActivationParser.Parse(trimmed);
                            if (parsed.Uris.Count > 0)
                            {
                                await _window.HandleActivationAsync(parsed);
                            }
                            else
                            {
                                _window.BringToFront();
                            }

                            return IpcProtocol.BuildResponse(
                                new IpcResponse(true, null, null, null));
                        }

                        var request = IpcProtocol.ParseRequest(trimmed);
                        if (request is null)
                        {
                            return IpcProtocol.BuildResponse(
                                new IpcResponse(false, "Invalid request.", null, null));
                        }

                        return IpcProtocol.BuildResponse(_window.ExecuteIpcCommand(request));
                    }).Task.Unwrap();

                    await writer.WriteLineAsync(response).ConfigureAwait(false);
                }
                finally
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                try
                {
                    await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static async Task<bool> ForwardToRunningInstanceAsync(string payload)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                await using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                await client.ConnectAsync(200).ConfigureAwait(false);
                await using var writer = new StreamWriter(
                    client,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };
                await writer.WriteLineAsync(payload).ConfigureAwait(false);
                return true;
            }
            catch
            {
                await Task.Delay(150).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static async Task<string?> DispatchNativeMessageAsync(string requestJson)
    {
        try
        {
            using var document = JsonDocument.Parse(requestJson);
            var type = document.RootElement.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;
            if (type == "ping")
            {
                var version = Assembly.GetEntryAssembly()
                    ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                return JsonSerializer.Serialize(new { ok = true, version });
            }

            if (type == "add" &&
                document.RootElement.TryGetProperty("url", out var urlElement) &&
                urlElement.GetString() is { Length: > 0 } url)
            {
                var connections = document.RootElement.TryGetProperty("connections", out var connectionsElement) &&
                                  connectionsElement.TryGetInt32(out var parsedConnections)
                    ? parsedConnections.ToString()
                    : null;
                var payload = $"hyperdl://add?url={Uri.EscapeDataString(url)}" +
                              (connections is not null ? $"&connections={connections}" : string.Empty);
                var forwarded = await ForwardToRunningInstanceAsync(payload).ConfigureAwait(false);
                return forwarded
                    ? JsonSerializer.Serialize(new { ok = true })
                    : JsonSerializer.Serialize(new { ok = false, error = "HyperDL is not running" });
            }

            return JsonSerializer.Serialize(new { ok = false, error = "unknown type" });
        }
        catch (Exception exception)
        {
            return JsonSerializer.Serialize(new { ok = false, error = exception.Message });
        }
    }

    public static void ApplyTheme(bool dark)
    {
        var source = new Uri(
            $"Themes/{(dark ? "Dark" : "Light")}.xaml",
            UriKind.Relative);
        var dictionary = new ResourceDictionary { Source = source };
        Application.Current.Resources.MergedDictionaries.Clear();
        Application.Current.Resources.MergedDictionaries.Add(dictionary);
    }

    private static HyperDLSettings LoadSettings(out string? error)
    {
        try
        {
            error = null;
            return UserSettingsStore.Load();
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return new HyperDLSettings();
        }
    }
}
