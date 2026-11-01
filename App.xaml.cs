using System.IO;
using System.IO.Pipes;
using System.Text;
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

        var settings = LoadSettings(out var settingsError);
        LocalizationManager.Instance.Apply(settings.Language);

        var payload = string.Join(' ', e.Args);
        var parsed = ParsePayload(payload);

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
                parsed.Uris,
                parsed.External,
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

    private async Task RunPipeServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    continue;
                }

                var payload = line.Trim();
                var operation = Dispatcher.InvokeAsync(async () =>
                {
                    if (_window is null)
                    {
                        return;
                    }

                    var parsed = ParsePayload(payload);
                    if (parsed.Uris.Count > 0)
                    {
                        await _window.HandleActivationAsync(parsed.Uris, parsed.External);
                    }
                    else
                    {
                        _window.BringToFront();
                    }
                });
                await operation.Task.Unwrap();
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

    private static async Task ForwardToRunningInstanceAsync(string payload)
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
                return;
            }
            catch
            {
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
    }

    private static (IReadOnlyList<Uri> Uris, bool External, string? InvalidText) ParsePayload(
        string? payload)
    {
        var uris = new List<Uri>();
        var invalid = new List<string>();
        var external = false;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (uris, false, null);
        }

        var tokens = payload.Split(
            ['\r', '\n', '\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].Trim().Trim('"', '\'', '<', '>');
            if (token.StartsWith("hyperdl:", StringComparison.OrdinalIgnoreCase))
            {
                external = true;
                var target = ParseProtocolUrl(token);
                if (target is not null)
                {
                    uris.Add(target);
                }
                else
                {
                    invalid.Add(token);
                }

                continue;
            }

            if ((token.Equals("--add", StringComparison.OrdinalIgnoreCase) ||
                 token.Equals("--url", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < tokens.Length)
            {
                token = tokens[++index].Trim('"', '\'', '<', '>');
            }

            if (Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
                DownloadUrlValidator.IsSupported(uri))
            {
                uris.Add(uri);
            }
            else if (token.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                invalid.Add(token);
            }
        }

        var distinct = uris
            .DistinctBy(uri => uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped))
            .ToArray();
        return (distinct, external, invalid.Count > 0 ? string.Join(' ', invalid) : null);
    }

    private static Uri? ParseProtocolUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(ProtocolRegistration.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var queryUrl = GetQueryParameter(uri, "url");
        if (queryUrl is not null &&
            Uri.TryCreate(queryUrl, UriKind.Absolute, out var fromQuery) &&
            DownloadUrlValidator.IsSupported(fromQuery))
        {
            return fromQuery;
        }

        var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(path, UriKind.Absolute, out var fromPath) &&
            DownloadUrlValidator.IsSupported(fromPath))
        {
            return fromPath;
        }

        return null;
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator >= 0 ? part[..separator] : part;
            if (!Uri.UnescapeDataString(key).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = separator >= 0 ? part[(separator + 1)..] : string.Empty;
            return Uri.UnescapeDataString(raw.Replace('+', ' '));
        }

        return null;
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
