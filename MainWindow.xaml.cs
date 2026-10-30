using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private readonly IReadOnlyList<Uri> _initialUris;
    private readonly bool _externalActivation;
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
    private ICollectionView? _jobsView;

    public MainWindow(
        HyperDLSettings settings,
        IReadOnlyList<Uri>? initialUris = null,
        bool externalActivation = false,
        string? startupError = null)
    {
        InitializeComponent();
        _settings = settings;
        _initialUris = initialUris ?? [];
        _externalActivation = externalActivation;
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
    }

    private static string L(string key) => LocalizationManager.GetString(key);

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
        if (_initialUris.Count > 0)
        {
            await HandleActivationAsync(_initialUris, _externalActivation);
        }

        UrlTextBox.Focus();
        UpdateCommandStates();
        SetStatus(L("ReadyStatus"));
    }

    /// <summary>
    /// Entry point for CLI arguments and the single-instance pipe.
    /// </summary>
    public Task HandleActivationAsync(IReadOnlyList<Uri> uris, bool external)
    {
        if (uris.Count == 0)
        {
            BringToFront();
            return Task.CompletedTask;
        }

        BringToFront();

        if (external && _settings.ConfirmExternalDownloads)
        {
            var preview = string.Join(Environment.NewLine, uris.Take(5));
            if (uris.Count > 5)
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
        foreach (var uri in uris)
        {
            if (_manager.AddUrl(uri))
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

            var engineChanged =
                !string.Equals(folder, _settings.DownloadDirectory, StringComparison.OrdinalIgnoreCase) ||
                perHost != _settings.MaxConnectionsPerHost;

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
        UpdateCommandStates();
    }

    private void Manager_JobFailed(object? sender, DownloadJob job)
    {
        if (job.ErrorKind is { } kind && kind != DownloadErrorKind.Cancelled)
        {
            SetStatus(job.ErrorText);
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

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        _timer.Stop();
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

        _allowClose = true;
        Close();
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
