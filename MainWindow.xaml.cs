using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using HyperDL.Core;
using HyperDL.Localization;
using HyperDL.Platform;

namespace HyperDL;

public partial class MainWindow : Window
{
    private readonly ActivationRequest? _activation;
    private readonly string? _startupError;
    private readonly DispatcherTimer _timer = new()
    {
        Interval = TimeSpan.FromMilliseconds(500)
    };

    private HyperDLSettings _settings;
    private DownloadEngine _engine;
    private DownloadManager _manager;
    private bool _initializing = true;
    private bool _loaded;
    private bool _allowClose;
    private bool _reallyExit;
    private bool _shutdownPrompted;
    private string? _pendingInstallerPath;
    private string? _lastClipboardUrl;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private DispatcherTimer? _clipboardTimer;
    private ICollectionView? _jobsView;

    public MainWindow(
        HyperDLSettings settings,
        ActivationRequest? activation = null,
        string? startupError = null)
    {
        InitializeComponent();
        _settings = settings;
        _activation = activation;
        _startupError = startupError;

        _engine = CreateEngine(_settings);
        GlobalConnectionLimiter.Shared.SetLimit(_settings.GlobalConnectionLimit);
        _manager = new DownloadManager(
            _engine,
            new HttpProbe(),
            new DownloadDatabase(),
            _settings,
            SynchronizationContext.Current);
        _manager.JobCompleted += Manager_JobCompleted;
        _manager.JobFailed += Manager_JobFailed;
        JobsGrid.ItemsSource = _manager.Jobs;
        _jobsView = CollectionViewSource.GetDefaultView(JobsGrid.ItemsSource);
        _jobsView.Filter = FilterJob;

        ApplyWindowBounds();
        ApplyColumnWidths();
        SelectLanguageItem();
        _timer.Tick += Timer_Tick;
        ApplyLocalization();
        InitializeTrayIcon();
        ApplyClipboardMonitoring();
    }

    private static string L(string key) => LocalizationManager.GetString(key);

    private void InitializeTrayIcon()
    {
        try
        {
            var icon = string.IsNullOrWhiteSpace(Environment.ProcessPath) ||
                       !File.Exists(Environment.ProcessPath)
                ? System.Drawing.SystemIcons.Application
                : System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath);
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = icon,
                Text = LocalizationManager.GetString("AppTitle"),
                Visible = true
            };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(LocalizationManager.GetString("AppTitle"), null,
                (_, _) => Dispatcher.Invoke(ShowFromTray));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(LocalizationManager.GetString("PauseAllButton"), null,
                (_, _) => Dispatcher.Invoke(() =>
                {
                    _manager.PauseAll();
                    UpdateCommandStates();
                }));
            menu.Items.Add(LocalizationManager.GetString("ResumeAllButton"), null,
                (_, _) => Dispatcher.Invoke(() =>
                {
                    _manager.ResumeAll();
                    UpdateCommandStates();
                }));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(LocalizationManager.GetString("TrayExitButton"), null,
                (_, _) => Dispatcher.Invoke(ExitFromTray));
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
        }
        catch
        {
            // A missing icon or a locked tray must never break the download window.
            _trayIcon = null;
        }
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitFromTray()
    {
        _reallyExit = true;
        Close();
    }

    private void NotifyJobFinished(bool success, DownloadJob job)
    {
        if (!_settings.ShowNotifications || _trayIcon is null)
        {
            return;
        }

        try
        {
            var title = L(success ? "DownloadCompleteTitle" : "DownloadFailedTitle");
            var body = success
                ? string.Format(L("DownloadCompleteFormat"), job.FileName)
                : job.ErrorText;
            _trayIcon.ShowBalloonTip(
                3000,
                title,
                body,
                System.Windows.Forms.ToolTipIcon.Info);
        }
        catch
        {
            // Balloon tips are best effort.
        }
    }

    private void MaybeOfferShutdown()
    {
        if (!_settings.ShutdownWhenComplete || _shutdownPrompted)
        {
            return;
        }

        var jobs = _manager.Jobs;
        if (!jobs.Any(job => job.Status == DownloadSessionStatus.Completed) ||
            jobs.Any(job => job.Status is DownloadSessionStatus.Downloading or
                                       DownloadSessionStatus.Waiting or
                                       DownloadSessionStatus.Pausing or
                                       DownloadSessionStatus.Paused))
        {
            return;
        }

        _shutdownPrompted = true;
        var answer = MessageBox.Show(
            this,
            L("ShutdownConfirmMessage"),
            L("ShutdownConfirmTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 30")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            SetStatus(L("ShutdownConfirmMessage"));
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("ShutdownConfirmTitle"),
                exception.Message,
                MessageBoxImage.Warning);
        }
    }

    private void ApplyClipboardMonitoring()
    {
        if (_settings.ClipboardMonitoring && _clipboardTimer is null)
        {
            _clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();
        }
        else if (!_settings.ClipboardMonitoring && _clipboardTimer is not null)
        {
            _clipboardTimer.Stop();
            _clipboardTimer.Tick -= ClipboardTimer_Tick;
            _clipboardTimer = null;
        }
    }

    private void ClipboardTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            var text = Clipboard.GetText().Trim();
            if (string.IsNullOrWhiteSpace(text) ||
                text == _lastClipboardUrl ||
                text == UrlTextBox.Text)
            {
                return;
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                !DownloadUrlValidator.IsSupported(uri))
            {
                return;
            }

            _lastClipboardUrl = text;
            UrlTextBox.Text = text;
            SetStatus(L("ClipboardUrlReady"));
        }
        catch
        {
            // The clipboard can be locked by another process; skip this tick.
        }
    }

    private void PasteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : string.Empty;
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                DownloadUrlValidator.IsSupported(uri))
            {
                UrlTextBox.Text = text;
                UrlTextBox.Focus();
                UrlTextBox.CaretIndex = text.Length;
            }
            else
            {
                SetStatus(L("ClipboardEmpty"));
            }
        }
        catch
        {
            SetStatus(L("ClipboardEmpty"));
        }
    }

    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private DownloadJob? SelectedJob => JobsGrid.SelectedItem as DownloadJob;

    private IReadOnlyList<DownloadJob> SelectedJobs =>
        JobsGrid.SelectedItems.OfType<DownloadJob>().ToArray();

    private DataGridColumn[] GridColumns =>
    [
        FileColumn,
        SizeColumn,
        ProgressColumn,
        SpeedColumn,
        EtaColumn,
        ConnectionsColumn,
        BufferColumn,
        StatusColumn
    ];

    private DownloadEngine CreateEngine(HyperDLSettings settings) =>
        new(
            client: null,
            downloadDirectory: settings.DownloadDirectory,
            options: new DownloadEngineOptions
            {
                BufferMode = settings.BufferMode,
                DynamicSegmentation = settings.DynamicSegmentation,
                MaximumConnectionsPerHost = settings.MaxConnectionsPerHost,
                SpeedLimitKBps = settings.SpeedLimitKBps
            });

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _timer.Start();

        if (!_settings.LegalUseAcknowledged)
        {
            MessageBox.Show(
                this,
                L("LegalMessage"),
                L("LegalTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _settings = _settings with { LegalUseAcknowledged = true };
            await SaveSettingsQuietlyAsync();
        }

        try
        {
            await _manager.InitializeAsync(_settings.AutoResume);
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("DatabaseErrorTitle"),
                string.Format(L("DatabaseErrorFormat"), exception.Message),
                MessageBoxImage.Error);
        }

        if (!string.IsNullOrWhiteSpace(_startupError))
        {
            ShowMessage(
                L("SettingsLoadErrorTitle"),
                string.Format(L("SettingsLoadErrorFormat"), _startupError),
                MessageBoxImage.Warning);
        }

        _initializing = false;
        if (_activation is { Uris.Count: > 0 } request)
        {
            await HandleActivationAsync(request);
        }

        UrlTextBox.Focus();
        UpdateCommandStates();
        SetStatus(L("ReadyStatus"));
        _ = CheckForUpdatesQuietlyAsync();
    }

    private async Task CheckForUpdatesQuietlyAsync()
    {
        if (!_settings.CheckForUpdates)
        {
            return;
        }

        try
        {
            var info = await UpdateChecker.CheckLatestAsync().ConfigureAwait(true);
            if (info is null)
            {
                return;
            }

            var current = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (!UpdateCheck.IsNewerVersion(current, info.Tag))
            {
                return;
            }

            if (!_settings.AutoUpdateDownload || info.InstallerUrl is null || info.SumsUrl is null)
            {
                SetStatus(string.Format(L("UpdateAvailableFormat"), info.Tag, info.Url));
                return;
            }

            // Auto-update: download into a temp folder, verify against the
            // release checksums, then ask before running the installer.
            var progress = new Progress<double>(value =>
                SetStatus($"HyperDL {info.Tag} — {value:0}%"));
            var downloaded = await UpdateInstaller.DownloadAndVerifyAsync(
                info,
                Path.Combine(Path.GetTempPath(), "HyperDL-update"),
                progress).ConfigureAwait(true);
            if (downloaded is null || !downloaded.Verified)
            {
                SetStatus(L("UpdateDownloadFailed"));
                return;
            }

            SetStatus(string.Format(L("UpdateVerifiedFormat"), info.Tag));
            var answer = MessageBox.Show(
                this,
                string.Format(L("UpdateReadyMessage"), info.Tag),
                L("UpdateReadyTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                _pendingInstallerPath = downloaded.InstallerPath;
                SetStatus(string.Format(L("UpdateVerifiedFormat"), info.Tag));
                return;
            }

            _pendingInstallerPath = downloaded.InstallerPath;
            _reallyExit = true;
            Close();
        }
        catch
        {
            // Update checks are best effort and never disturb startup.
        }
    }

    /// <summary>
    /// Executes a CLI/pipe command on the UI thread and returns its JSON
    /// response payload.
    /// </summary>
    public IpcResponse ExecuteIpcCommand(IpcRequest request)
    {
        switch (request.Command)
        {
            case "list":
            {
                var jobs = _manager.Jobs.Select(job => new IpcJobSnapshot(
                    job.Id,
                    job.FileName,
                    job.StatusText,
                    job.Percentage ?? 0,
                    job.BytesDownloaded,
                    job.TotalBytes,
                    job.TargetPath,
                    job.ActualSha256)).ToArray();
                return new IpcResponse(true, null, jobs, null);
            }

            case "pause-all":
                _manager.PauseAll();
                UpdateCommandStates();
                return new IpcResponse(true, null, null, null);

            case "resume-all":
                _manager.ResumeAll();
                UpdateCommandStates();
                return new IpcResponse(true, null, null, null);

            case "clear-completed":
            {
                var completed = _manager.Jobs.Where(job => job.IsCompleted).ToArray();
                foreach (var job in completed)
                {
                    _ = RemoveCompletedQuietlyAsync(job);
                }

                return new IpcResponse(true, null, null, completed.Length);
            }

            case "add":
            {
                var uris = request.Urls
                    .Select(text => Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                                    DownloadUrlValidator.IsSupported(uri)
                        ? uri
                        : null)
                    .Where(uri => uri is not null)
                    .Cast<Uri>()
                    .DistinctBy(uri => uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped))
                    .ToArray();
                var added = 0;
                foreach (var uri in uris)
                {
                    if (_manager.AddUrl(uri, request.Connections))
                    {
                        added++;
                    }
                }

                if (added > 0)
                {
                    _shutdownPrompted = false;
                    UpdateCommandStates();
                }

                return new IpcResponse(true, null, null, added);
            }

            default:
                return new IpcResponse(false, $"Unknown command '{request.Command}'.", null, null);
        }
    }

    private async Task RemoveCompletedQuietlyAsync(DownloadJob job)
    {
        try
        {
            await _manager.RemoveAsync(job).ConfigureAwait(true);
            await Dispatcher.InvokeAsync(UpdateCommandStates);
        }
        catch
        {
            // Removal is best effort from the CLI path.
        }
    }

    /// <summary>
    /// Entry point for CLI arguments and the single-instance pipe. The app
    /// setting always confirms external activations; the extension's confirm=1
    /// adds a confirmation back, but confirm=0 never bypasses the setting.
    /// </summary>
    public Task HandleActivationAsync(ActivationRequest request)
    {
        if (request.Uris.Count == 0)
        {
            BringToFront();
            return Task.CompletedTask;
        }

        BringToFront();

        var showConfirm = _settings.ConfirmExternalDownloads || request.Confirm == true;
        if (request.External && showConfirm)
        {
            var preview = string.Join(Environment.NewLine, request.Uris.Take(5));
            if (request.Uris.Count > 5)
            {
                preview += Environment.NewLine + "…";
            }

            var answer = MessageBox.Show(
                this,
                string.Format(L("ExternalDownloadMessage"), preview),
                L("ExternalDownloadTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                SetStatus(L("ReadyStatus"));
                return Task.CompletedTask;
            }
        }

        var added = 0;
        var skipped = 0;
        foreach (var uri in request.Uris)
        {
            if (_manager.AddUrl(uri, request.Connections))
            {
                added++;
            }
            else
            {
                skipped++;
            }
        }

        if (added > 0)
        {
            _shutdownPrompted = false;
            SetStatus(string.Format(L("BatchAddedFormat"), added));
        }
        else if (skipped > 0)
        {
            SetStatus(string.Format(L("DuplicateSkippedFormat"), skipped));
        }

        UpdateCommandStates();
        return Task.CompletedTask;
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e) =>
        await AddFromInputAsync();

    private async void UrlTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await AddFromInputAsync();
        }
    }

    private async Task AddFromInputAsync()
    {
        var text = UrlTextBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var result = await _manager.AddTextAsync(text);
        if (result.Added > 0)
        {
            UrlTextBox.Clear();
            _shutdownPrompted = false;
            SetStatus(string.Format(L("BatchAddedFormat"), result.Added));
            if (result.Skipped > 0)
            {
                SetStatus(
                    string.Format(L("BatchAddedFormat"), result.Added) + " " +
                    string.Format(L("DuplicateSkippedFormat"), result.Skipped));
            }
        }
        else if (result.Invalid > 0)
        {
            ShowMessage(L("InvalidUrlTitle"), L("InvalidUrlMessage"), MessageBoxImage.Warning);
        }
        else if (result.Skipped > 0)
        {
            SetStatus(string.Format(L("DuplicateSkippedFormat"), result.Skipped));
        }

        UpdateCommandStates();
    }

    private async Task AddTextAsync(string text)
    {
        var result = await _manager.AddTextAsync(text);
        if (result.Added > 0)
        {
            SetStatus(string.Format(L("BatchAddedFormat"), result.Added));
        }
        else if (result.Invalid > 0)
        {
            ShowMessage(L("InvalidUrlTitle"), L("InvalidUrlMessage"), MessageBoxImage.Warning);
        }
        else if (result.Skipped > 0)
        {
            SetStatus(string.Format(L("DuplicateSkippedFormat"), result.Skipped));
        }

        UpdateCommandStates();
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in SelectedJobs.Where(job => job.CanPause))
        {
            _manager.Pause(job);
        }

        UpdateCommandStates();
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in SelectedJobs.Where(job => job.CanResume))
        {
            _manager.Resume(job);
        }

        UpdateCommandStates();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in SelectedJobs.Where(job => job.CanCancel))
        {
            _manager.Cancel(job);
        }

        UpdateCommandStates();
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in SelectedJobs.Where(job => job.CanRetry))
        {
            _manager.Retry(job);
        }

        UpdateCommandStates();
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e) =>
        await RemoveSelectedAsync(deleteFiles: false);

    private async Task RemoveSelectedAsync(bool deleteFiles)
    {
        var jobs = SelectedJobs;
        if (jobs.Count == 0)
        {
            SetStatus(L("NoSelectionMessage"));
            return;
        }

        var single = jobs.Count == 1 ? jobs[0] : null;
        var message = deleteFiles
            ? single is not null
                ? string.Format(L("ConfirmDeleteFileMessage"), single.FileName)
                : string.Format(L("ConfirmDeleteMultipleFilesMessage"), jobs.Count)
            : single is not null
                ? string.Format(
                    L(single.IsCompleted
                        ? "ConfirmRemoveCompletedMessage"
                        : "ConfirmRemoveMessage"),
                    single.FileName)
                : string.Format(L("ConfirmRemoveMultipleMessage"), jobs.Count);
        var title = L(deleteFiles ? "ConfirmDeleteFileTitle" : "ConfirmRemoveTitle");
        if (MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (deleteFiles)
        {
            var failures = await _manager.DeleteFilesAsync(jobs);
            if (failures.Count > 0)
            {
                ShowMessage(
                    L("DeleteFileButton"),
                    string.Format(L("DeleteFileErrorFormat"), string.Join(
                        Environment.NewLine,
                        failures)),
                    MessageBoxImage.Warning);
            }
            else
            {
                SetStatus(string.Format(L("DeleteFileFormat"), jobs.Count));
            }
        }
        else
        {
            foreach (var job in jobs)
            {
                await _manager.RemoveAsync(job);
            }

            SetStatus(L("ReadyStatus"));
        }

        UpdateCommandStates();
    }

    private async void ClearCompletedButton_Click(object sender, RoutedEventArgs e)
    {
        var completed = _manager.Jobs.Where(job => job.IsCompleted).ToArray();
        if (completed.Length == 0)
        {
            SetStatus(L("NoCompletedMessage"));
            return;
        }

        if (MessageBox.Show(
                this,
                string.Format(L("ConfirmClearCompletedMessage"), completed.Length),
                L("ConfirmClearCompletedTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var job in completed)
        {
            await _manager.RemoveAsync(job);
        }

        SetStatus(string.Format(L("ClearCompletedFormat"), completed.Length));
        UpdateCommandStates();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _jobsView?.Refresh();

    private void OpenLogsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticsLog.DirectoryPath);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{DiagnosticsLog.DirectoryPath}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("OpenLogsButton"),
                string.Format(L("OpenFileFailedFormat"), exception.Message),
                MessageBoxImage.Error);
        }
    }

    private void JobsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(JobsGrid, source) is DataGridRow row &&
            row.Item is DownloadJob job &&
            !JobsGrid.SelectedItems.Contains(job))
        {
            JobsGrid.SelectedItem = job;
        }
    }

    private void ContextOpenFile_Click(object sender, RoutedEventArgs e) => OpenSelectedFile();

    private void ContextOpenFolder_Click(object sender, RoutedEventArgs e) => OpenSelectedFolder();

    private void ContextCopyUrl_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job)
        {
            return;
        }

        try
        {
            Clipboard.SetText(job.SourceUri.ToString());
            SetStatus(L("UrlCopied"));
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }

    private void ContextCopyDetails_Click(object sender, RoutedEventArgs e) =>
        CopySelectedDetails();

    private async void ContextVerifySha256_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job)
        {
            SetStatus(L("NoSelectionMessage"));
            return;
        }

        var path = job.TargetPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ShowMessage(
                L("VerifySha256Menu"),
                string.IsNullOrEmpty(path) ? L("StatusQueued") : path,
                MessageBoxImage.Warning);
            return;
        }

        var expected = TextPromptWindow.Prompt(
            this,
            L("VerifySha256Menu"),
            L("Sha256PromptLabel"),
            job.ActualSha256 ?? string.Empty);
        if (string.IsNullOrWhiteSpace(expected))
        {
            return;
        }

        expected = expected.Trim().ToLowerInvariant();
        SetStatus(L("Sha256Computing"));
        try
        {
            var actual = job.ActualSha256 ?? await FileHash.ComputeSha256Async(path);
            job.SetSha256(actual);
            if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage(L("VerifySha256Menu"), L("Sha256Matched"), MessageBoxImage.Information);
            }
            else
            {
                ShowMessage(
                    L("VerifySha256Menu"),
                    string.Format(L("Sha256Mismatched"), expected, actual),
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception exception)
        {
            ShowMessage(L("VerifySha256Menu"), exception.Message, MessageBoxImage.Error);
        }
        finally
        {
            SetStatus(L("ReadyStatus"));
        }
    }

    private async void ContextCopySha256_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job)
        {
            SetStatus(L("NoSelectionMessage"));
            return;
        }

        try
        {
            var hash = job.ActualSha256;
            if (hash is null && job.TargetPath is { Length: > 0 } path && File.Exists(path))
            {
                SetStatus(L("Sha256Computing"));
                hash = await FileHash.ComputeSha256Async(path);
                job.SetSha256(hash);
            }

            if (hash is null)
            {
                SetStatus(L("Sha256Computing"));
                return;
            }

            Clipboard.SetText(hash);
            SetStatus(L("Sha256Copied"));
        }
        catch
        {
            // The clipboard can be locked by another process.
        }
    }

    private void ContextSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job)
        {
            SetStatus(L("NoSelectionMessage"));
            return;
        }

        var value = TextPromptWindow.Prompt(
            this,
            L("ScheduleMenu"),
            L("SchedulePromptLabel"),
            job.ScheduleText ?? string.Empty);
        if (value is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            _manager.Schedule(job, null);
            SetStatus(L("ReadyStatus"));
            return;
        }

        if (!DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var start))
        {
            ShowMessage(L("ScheduleMenu"), L("ScheduleParseError"), MessageBoxImage.Warning);
            return;
        }

        _manager.Schedule(job, start);
        SetStatus(string.Format(L("ScheduledForFormat"), start.ToString("yyyy-MM-dd HH:mm")));
    }

    private async void ContextEditUrl_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedJob is not { } job)
        {
            return;
        }

        if (job.Session is not null)
        {
            ShowMessage(
                L("EditUrlTitle"),
                L("EditUrlUnavailableMessage"),
                MessageBoxImage.Information);
            return;
        }

        var value = TextPromptWindow.Prompt(
            this,
            L("EditUrlTitle"),
            L("EditUrlLabel"),
            job.SourceUri.ToString());
        if (value is null)
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !DownloadUrlValidator.IsSupported(uri))
        {
            ShowMessage(
                L("InvalidUrlTitle"),
                L("InvalidUrlMessage"),
                MessageBoxImage.Warning);
            return;
        }

        if (!await _manager.ReplaceUrlAsync(job, uri))
        {
            ShowMessage(
                L("EditUrlTitle"),
                job.Session is not null
                    ? L("EditUrlUnavailableMessage")
                    : L("EditUrlDuplicateMessage"),
                MessageBoxImage.Information);
            return;
        }

        SetStatus(L("UrlUpdatedMessage"));
        UpdateCommandStates();
    }

    private void ContextRetry_Click(object sender, RoutedEventArgs e) => RetryButton_Click(sender, e);

    private void ContextCancel_Click(object sender, RoutedEventArgs e) => CancelButton_Click(sender, e);

    private async void ContextRemove_Click(object sender, RoutedEventArgs e) =>
        await RemoveSelectedAsync(deleteFiles: false);

    private async void ContextDeleteFile_Click(object sender, RoutedEventArgs e) =>
        await RemoveSelectedAsync(deleteFiles: true);

    private void OpenFileButton_Click(object sender, RoutedEventArgs e) => OpenSelectedFile();

    private void JobsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedJob is { IsCompleted: true })
        {
            OpenSelectedFile();
        }
    }

    private void OpenSelectedFile()
    {
        if (SelectedJob?.TargetPath is not { Length: > 0 } path)
        {
            return;
        }

        if (!File.Exists(path))
        {
            ShowMessage(L("OpenFileButton"), path, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("OpenFileButton"),
                string.Format(L("OpenFileFailedFormat"), exception.Message),
                MessageBoxImage.Error);
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenSelectedFolder();

    private void OpenSelectedFolder()
    {
        string? path = null;
        var job = SelectedJob;
        if (job?.TargetPath is { Length: > 0 } target)
        {
            path = Path.GetDirectoryName(target);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = _settings.DownloadDirectory;
        }

        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ShowMessage(L("OpenFolderButton"), L("FolderMissingMessage"), MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("OpenFolderButton"),
                string.Format(L("OpenFileFailedFormat"), exception.Message),
                MessageBoxImage.Error);
        }
    }

    private void CopySelectedDetails()
    {
        var jobs = SelectedJobs;
        if (jobs.Count == 0)
        {
            return;
        }

        if (jobs.Count == 1 && jobs[0] is { } job)
        {
            var lines = new List<string>
            {
                $"{L("ColumnFile")}: {job.FileName}",
                $"{L("SourceLabel")}: {job.SourceUri}",
                $"{L("TargetLabel")}: {job.TargetPath ?? L("StatusQueued")}",
                $"{L("ColumnStatus")}: {job.StatusText}",
                $"{L("ColumnProgress")}: {job.ProgressText}",
                $"{L("ColumnSpeed")}: {job.SpeedText}",
                $"{L("ConnectionsDetailLabel")}: {job.ConnectionCount}",
                $"{L("SegmentsLabel")}: {job.SegmentCount}",
                $"{L("ColumnBuffer")}: {job.BufferModeText}"
            };
            if (!string.IsNullOrWhiteSpace(job.ErrorDetailsText))
            {
                lines.Add($"{L("ErrorLabel")}: {job.ErrorDetailsText}");
            }

            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine, lines));
                SetStatus(L("DetailsCopied"));
            }
            catch
            {
                // Clipboard can be locked by another process.
            }

            return;
        }

        try
        {
            Clipboard.SetText(string.Join(
                Environment.NewLine,
                jobs.Select(job => $"{job.FileName} | {job.StatusText} | {job.ProgressText} | {job.SourceUri}")));
            SetStatus(L("DetailsCopied"));
        }
        catch
        {
            // Clipboard can be locked by another process.
        }
    }

    private void CopyDetailsButton_Click(object sender, RoutedEventArgs e) => CopySelectedDetails();

    private void PauseAllButton_Click(object sender, RoutedEventArgs e)
    {
        _manager.PauseAll();
        UpdateCommandStates();
    }

    private void ResumeAllButton_Click(object sender, RoutedEventArgs e)
    {
        _manager.ResumeAll();
        UpdateCommandStates();
    }

    private void JobsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateCommandStates();

    private Point? _dragStartPoint;
    private DownloadJob? _dragJob;
    private const string DragFormat = "HyperDL-JobMove";

    private void JobsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(JobsGrid, source) is DataGridRow row &&
            row.Item is DownloadJob job)
        {
            _dragStartPoint = e.GetPosition(null);
            _dragJob = job;
        }
        else
        {
            _dragStartPoint = null;
            _dragJob = null;
        }
    }

    private void JobsGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragJob is null || _dragStartPoint is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(null) - _dragStartPoint.Value;
        if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
        {
            var data = new DataObject(DragFormat, _dragJob);
            try
            {
                DragDrop.DoDragDrop(JobsGrid, data, DragDropEffects.Move);
            }
            finally
            {
                _dragStartPoint = null;
                _dragJob = null;
            }
        }
    }

    private void JobsGrid_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not DownloadJob job ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(JobsGrid, source) is not DataGridRow targetRow ||
            targetRow.Item is not DownloadJob target)
        {
            return;
        }

        var targetIndex = _manager.Jobs.IndexOf(target);
        _manager.MoveJob(job, targetIndex);
        JobsGrid.SelectedItem = job;
        e.Handled = true;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        var jobs = _manager.Jobs;
        var active = 0;
        var activeConnections = 0;
        double speed = 0;
        double percentageTotal = 0;

        foreach (var job in jobs)
        {
            if (job.Status == DownloadSessionStatus.Downloading)
            {
                activeConnections += job.ActiveConnections;
                speed += job.BytesPerSecond;
                percentageTotal += job.Percentage ?? 0;
            }
        }

        active = _manager.Jobs.Count(j => j.Status == DownloadSessionStatus.Downloading);

        ActiveSummaryTextBlock.Text = active > 0
            ? string.Format(L("ActiveDownloadsFormat"), active)
            : string.Empty;
        SpeedSummaryTextBlock.Text = speed > 0
            ? string.Format(L("TotalSpeedFormat"), FormatBytes(speed))
            : string.Empty;
        ConnectionSummaryTextBlock.Text = activeConnections > 0
            ? string.Format(L("GlobalConnectionsFormat"), activeConnections)
            : string.Empty;

        if (TaskbarProgress is not null)
        {
            if (active > 0)
            {
                TaskbarProgress.ProgressState = TaskbarItemProgressState.Normal;
                TaskbarProgress.ProgressValue = Math.Clamp(
                    percentageTotal / active / 100d,
                    0,
                    1);
            }
            else
            {
                TaskbarProgress.ProgressState = TaskbarItemProgressState.None;
                TaskbarProgress.ProgressValue = 0;
            }
        }

        UpdateCommandStates();
    }

    private void UpdateCommandStates()
    {
        var jobs = SelectedJobs;
        var job = SelectedJob;
        var hasSelection = jobs.Count > 0;
        var single = jobs.Count == 1 ? jobs[0] : null;

        PauseButton.IsEnabled = jobs.Any(j => j.CanPause);
        ResumeButton.IsEnabled = jobs.Any(j => j.CanResume);
        CancelButton.IsEnabled = jobs.Any(j => j.CanCancel);
        RetryButton.IsEnabled = jobs.Any(j => j.CanRetry);
        RemoveButton.IsEnabled = hasSelection;
        OpenFileButton.IsEnabled = single?.TargetPath is { Length: > 0 } path && File.Exists(path);
        OpenFolderButton.IsEnabled = hasSelection;
        CopyDetailsButton.IsEnabled = hasSelection;
        ClearCompletedButton.IsEnabled = _manager.Jobs.Any(j => j.IsCompleted);
    }

    private bool FilterJob(object item)
    {
        if (item is not DownloadJob job)
        {
            return false;
        }

        var filter = SearchTextBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return job.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               job.SourceUri.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase) ||
               job.StatusText.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyColumnWidths()
    {
        try
        {
            if (_settings.ColumnWidths.Count == 0)
            {
                return;
            }

            var columns = GridColumns;
            foreach (var column in columns)
            {
                var name = column.GetType().GetProperty("Name")?.GetValue(column)?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (_settings.ColumnWidths.TryGetValue(name, out var width) &&
                    width > 10)
                {
                    column.Width = new DataGridLength(width);
                }
            }
        }
        catch
        {
            // Column widths are best effort.
        }
    }

    private void SaveColumnWidths()
    {
        try
        {
            var widths = new Dictionary<string, double>();
            foreach (var column in GridColumns)
            {
                var name = column.GetType().GetProperty("Name")?.GetValue(column)?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (column.ActualWidth > 10)
                {
                    widths[name] = column.ActualWidth;
                }
            }

            _settings = _settings with { ColumnWidths = widths };
        }
        catch
        {
            // Best effort.
        }
    }


    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        FolderTextBox.Text = _settings.DownloadDirectory;
        SelectComboByTag(ConnectionsComboBox, _settings.ConnectionCount.ToString());
        SelectComboByTag(ConcurrentComboBox, _settings.MaximumConcurrentDownloads.ToString());
        SelectComboByTag(BufferModeComboBox, _settings.BufferMode.ToString());
        PerHostTextBox.Text = _settings.MaxConnectionsPerHost.ToString();
        SpeedLimitTextBox.Text = _settings.SpeedLimitKBps.ToString();
        DynamicSegmentationCheckBox.IsChecked = _settings.DynamicSegmentation;
        AutoResumeCheckBox.IsChecked = _settings.AutoResume;
        ConfirmExternalCheckBox.IsChecked = _settings.ConfirmExternalDownloads;
        MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
        ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
        ClipboardMonitoringCheckBox.IsChecked = _settings.ClipboardMonitoring;
        ShutdownWhenCompleteCheckBox.IsChecked = _settings.ShutdownWhenComplete;
        CheckForUpdatesCheckBox.IsChecked = _settings.CheckForUpdates;
        AutoUpdateCheckBox.IsChecked = _settings.AutoUpdateDownload;
        Http2CheckBox.IsChecked = _settings.Http2Enabled;
        DarkModeCheckBox.IsChecked = _settings.DarkMode;
        NativeMessagingCheckBox.IsChecked = _settings.NativeMessagingEnabled;
        ExtensionIdTextBox.Text = _settings.ExtensionId;
        SettingsOverlay.Visibility = Visibility.Visible;
        FolderTextBox.Focus();
    }

    private void CloseSettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsOverlay.Visibility = Visibility.Collapsed;

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = L("DownloadFolderLabel"),
            InitialDirectory = Directory.Exists(FolderTextBox.Text)
                ? FolderTextBox.Text
                : _settings.DownloadDirectory
        };
        if (dialog.ShowDialog(this) == true)
        {
            FolderTextBox.Text = dialog.FolderName;
        }
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = FolderTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new InvalidDataException(L("DownloadFolderLabel"));
            }

            Directory.CreateDirectory(folder);

            if (!int.TryParse(PerHostTextBox.Text.Trim(), out var perHost) ||
                perHost is < 0 or > DownloadEngine.MaximumSegmentCount)
            {
                throw new InvalidDataException(L("PerHostLabel"));
            }

            var connections = ReadComboTag(ConnectionsComboBox, "0");
            var concurrent = ReadComboTag(ConcurrentComboBox, "2");
            var bufferTag =
                (BufferModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ??
                BufferMode.Auto.ToString();
            if (!BufferModeSizing.TryParse(bufferTag, out var bufferMode))
            {
                bufferMode = BufferMode.Auto;
            }

            if (!int.TryParse(SpeedLimitTextBox.Text.Trim(), out var speedLimit) ||
                speedLimit is < 0 or > 1_000_000)
            {
                speedLimit = 0;
            }

            var http2 = Http2CheckBox.IsChecked == true;

            var engineChanged =
                !string.Equals(folder, _settings.DownloadDirectory, StringComparison.OrdinalIgnoreCase) ||
                perHost != _settings.MaxConnectionsPerHost ||
                http2 != _settings.Http2Enabled;

            var updated = _settings with
            {
                DownloadDirectory = folder,
                ConnectionCount = connections,
                MaximumConcurrentDownloads = concurrent,
                BufferMode = bufferMode,
                MaxConnectionsPerHost = perHost,
                DynamicSegmentation = DynamicSegmentationCheckBox.IsChecked == true,
                AutoResume = AutoResumeCheckBox.IsChecked == true,
                ConfirmExternalDownloads = ConfirmExternalCheckBox.IsChecked == true,
                MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true,
                ShowNotifications = ShowNotificationsCheckBox.IsChecked == true,
                ClipboardMonitoring = ClipboardMonitoringCheckBox.IsChecked == true,
                ShutdownWhenComplete = ShutdownWhenCompleteCheckBox.IsChecked == true,
                CheckForUpdates = CheckForUpdatesCheckBox.IsChecked == true,
                AutoUpdateDownload = AutoUpdateCheckBox.IsChecked == true,
                Http2Enabled = Http2CheckBox.IsChecked == true,
                DarkMode = DarkModeCheckBox.IsChecked == true,
                NativeMessagingEnabled = NativeMessagingCheckBox.IsChecked == true,
                ExtensionId = ExtensionIdTextBox.Text.Trim(),
                SpeedLimitKBps = speedLimit,
                WindowWidth = Width,
                WindowHeight = Height,
                WindowLeft = Left,
                WindowTop = Top
            };

            UserSettingsStore.Validate(updated);
            await UserSettingsStore.SaveAsync(updated);

            _settings = updated;
            _manager.Settings = updated;
            GlobalConnectionLimiter.Shared.SetLimit(updated.GlobalConnectionLimit);
            _engine.SetSpeedLimit(updated.SpeedLimitKBps);
            ApplyClipboardMonitoring();
            App.ApplyTheme(updated.DarkMode);
            if (updated.NativeMessagingEnabled && updated.ExtensionId.Length > 0)
            {
                try
                {
                    ProtocolRegistration.RegisterNativeHost(updated.ExtensionId);
                }
                catch (Exception nativeException)
                {
                    ShowMessage(L("SettingsTitle"), nativeException.Message, MessageBoxImage.Warning);
                }
            }
            else
            {
                ProtocolRegistration.UnregisterNativeHost();
            }

            if (engineChanged)
            {
                _engine = CreateEngine(updated);
                _manager.UpdateEngine(_engine);
            }

            SettingsOverlay.Visibility = Visibility.Collapsed;
            SetStatus(L("SettingsSavedMessage"));
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("SettingsTitle"),
                exception.Message,
                MessageBoxImage.Warning);
        }
    }

    private void RegisterProtocolButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ProtocolRegistration.Register();
            ShowMessage(
                L("BrowserRegisteredTitle"),
                L("BrowserRegisteredMessage"),
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("BrowserRegisteredTitle"),
                string.Format(L("BrowserRegisterFailedFormat"), exception.Message),
                MessageBoxImage.Error);
        }
    }

    private async void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "hyperdl-settings.json",
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await UserSettingsStore.SaveToAsync(_settings, dialog.FileName);
            SetStatus(L("SettingsImportedFormat"));
        }
        catch (Exception exception)
        {
            ShowMessage(L("SettingsTitle"), exception.Message, MessageBoxImage.Warning);
        }
    }

    private async void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON (*.json)|*.json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var imported = UserSettingsStore.LoadFrom(dialog.FileName);
            await ApplyImportedSettingsAsync(imported);
        }
        catch (Exception exception)
        {
            ShowMessage(
                L("SettingsTitle"),
                string.Format(L("SettingsInvalidFormat"), exception.Message),
                MessageBoxImage.Warning);
        }
    }

    private async Task ApplyImportedSettingsAsync(HyperDLSettings imported)
    {
        await UserSettingsStore.SaveAsync(imported);
        _settings = imported;
        _manager.Settings = imported;
        GlobalConnectionLimiter.Shared.SetLimit(imported.GlobalConnectionLimit);
        _engine = CreateEngine(imported);
        _manager.UpdateEngine(_engine);
        LocalizationManager.Instance.Apply(imported.Language);
        App.ApplyTheme(imported.DarkMode);
        ApplyClipboardMonitoring();
        foreach (var job in _manager.Jobs)
        {
            job.RefreshLocalization();
        }

        SelectLanguageItem();
        SelectComboByTag(ConnectionsComboBox, imported.ConnectionCount.ToString());
        SelectComboByTag(ConcurrentComboBox, imported.MaximumConcurrentDownloads.ToString());
        SelectComboByTag(BufferModeComboBox, imported.BufferMode.ToString());
        FolderTextBox.Text = imported.DownloadDirectory;
        PerHostTextBox.Text = imported.MaxConnectionsPerHost.ToString();
        SpeedLimitTextBox.Text = imported.SpeedLimitKBps.ToString();
        DynamicSegmentationCheckBox.IsChecked = imported.DynamicSegmentation;
        AutoResumeCheckBox.IsChecked = imported.AutoResume;
        ConfirmExternalCheckBox.IsChecked = imported.ConfirmExternalDownloads;
        MinimizeToTrayCheckBox.IsChecked = imported.MinimizeToTray;
        ShowNotificationsCheckBox.IsChecked = imported.ShowNotifications;
        ClipboardMonitoringCheckBox.IsChecked = imported.ClipboardMonitoring;
        ShutdownWhenCompleteCheckBox.IsChecked = imported.ShutdownWhenComplete;
        CheckForUpdatesCheckBox.IsChecked = imported.CheckForUpdates;
        AutoUpdateCheckBox.IsChecked = imported.AutoUpdateDownload;
        Http2CheckBox.IsChecked = imported.Http2Enabled;
        DarkModeCheckBox.IsChecked = imported.DarkMode;
        NativeMessagingCheckBox.IsChecked = imported.NativeMessagingEnabled;
        ExtensionIdTextBox.Text = imported.ExtensionId;
        UpdateCommandStates();
        SetStatus(L("SettingsImportedFormat"));
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || LanguageComboBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var tag = item.Tag?.ToString();
        if (!Enum.TryParse<AppLanguage>(tag, ignoreCase: true, out var language))
        {
            return;
        }

        _settings = _settings with { Language = language };
        LocalizationManager.Instance.Apply(language);
        ApplyLocalization();
        _ = SaveSettingsQuietlyAsync();
    }

    private void ApplyLocalization()
    {
        foreach (var job in _manager.Jobs)
        {
            job.RefreshLocalization();
        }

        UpdateCommandStates();
    }

    private void SelectLanguageItem()
    {
        SelectComboByTag(LanguageComboBox, _settings.Language.ToString());
    }

    private static void SelectComboByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem candidate &&
                string.Equals(candidate.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = candidate;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static int ReadComboTag(ComboBox comboBox, string fallback) =>
        int.TryParse(
            (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback,
            out var value)
            ? value
            : int.Parse(fallback);

    private void Manager_JobCompleted(object? sender, DownloadJob job)
    {
        SetStatus(string.Format(L("DownloadCompleteFormat"), job.FileName));
        NotifyJobFinished(success: true, job);
        MaybeOfferShutdown();
        UpdateCommandStates();
    }

    private void Manager_JobFailed(object? sender, DownloadJob job)
    {
        if (job.ErrorKind is { } kind && kind != DownloadErrorKind.Cancelled)
        {
            SetStatus(job.ErrorText);
            NotifyJobFinished(success: false, job);
        }

        UpdateCommandStates();
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.UnicodeText) ||
                    e.Data.GetDataPresent(DataFormats.Text)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        var text = e.Data.GetData(DataFormats.UnicodeText) as string ??
                   e.Data.GetData(DataFormats.Text) as string;
        if (!string.IsNullOrWhiteSpace(text))
        {
            await AddTextAsync(text);
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        base.OnClosed(e);
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        if (!_reallyExit && _settings.MinimizeToTray)
        {
            // Closing hides HyperDL into the notification tray; the tray menu
            // is the only place that performs a real exit.
            e.Cancel = true;
            Hide();
            return;
        }

        e.Cancel = true;
        _timer.Stop();
        _clipboardTimer?.Stop();
        SaveColumnWidths();
        _settings = _settings with
        {
            WindowWidth = Width,
            WindowHeight = Height,
            WindowLeft = Left,
            WindowTop = Top,
            ColumnWidths = _settings.ColumnWidths
        };
        await SaveSettingsQuietlyAsync();
        try
        {
            await _manager.ShutdownAsync();
            await _manager.DisposeAsync();
        }
        catch
        {
            // Closing must not throw.
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
        _allowClose = true;
        Close();

        if (_pendingInstallerPath is { Length: > 0 } installer && File.Exists(installer))
        {
            try
            {
                Process.Start(new ProcessStartInfo(installer, "/SILENT")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // Launching the installer is best effort; the file stays in
                // the temp folder for a manual run.
            }
        }
    }

    private void ApplyWindowBounds()
    {
        if (_settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight)
        {
            Width = _settings.WindowWidth;
            Height = _settings.WindowHeight;
        }

        if (_settings.WindowLeft is { } left && _settings.WindowTop is { } top)
        {
            var virtualLeft = SystemParameters.VirtualScreenLeft;
            var virtualTop = SystemParameters.VirtualScreenTop;
            var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
            var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
            if (left >= virtualLeft &&
                top >= virtualTop &&
                left + Width <= virtualRight &&
                top + Height <= virtualBottom)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = left;
                Top = top;
            }
        }
    }

    private async Task SaveSettingsQuietlyAsync()
    {
        try
        {
            await UserSettingsStore.SaveAsync(_settings);
        }
        catch
        {
            // Settings are best effort; a read-only profile must not break downloads.
        }
    }

    private void SetStatus(string message) => StatusMessageTextBlock.Text = message;

    private void ShowMessage(string title, string message, MessageBoxImage icon) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, icon);

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = Math.Abs(bytes);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
