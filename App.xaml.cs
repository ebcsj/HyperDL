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

                    var parsed = ActivationParser.Parse(payload);
                    if (parsed.Uris.Count > 0)
                    {
                        await _window.HandleActivationAsync(parsed);
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
