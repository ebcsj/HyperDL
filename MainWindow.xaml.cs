using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows;
using HyperDL.Core;

namespace HyperDL;

public partial class MainWindow : Window
{
    private DownloadEngine _downloadEngine = new();
    private readonly HttpProbe _httpProbe = new();
    private readonly DownloadDatabase _downloadDatabase = new();
    private readonly List<DownloadSession> _downloadHistory = [];
    private readonly ObservableCollection<DownloadListEntry> _downloadRows = [];
    private CancellationTokenSource? _downloadCancellation;
    private DownloadSession? _activeSession;
    private DownloadListEntry? _activeEntry;
    private HyperDLSettings _settings = new();

    public MainWindow()
    {
        InitializeComponent();
        DownloadListDataGrid.ItemsSource = _downloadRows;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = UserSettingsStore.Load();
            GlobalConnectionLimiter.Shared.SetLimit(_settings.GlobalConnectionLimit);
            _downloadEngine = new DownloadEngine(
                downloadDirectory: _settings.DownloadDirectory,
                lowRamMode: _settings.LowRamMode);
            SettingsDownloadDirectoryTextBox.Text = _settings.DownloadDirectory;
            MaximumConcurrentTextBox.Text = _settings.MaximumConcurrentDownloads.ToString();
            GlobalConnectionLimitTextBox.Text = _settings.GlobalConnectionLimit.ToString();
            LowRamModeCheckBox.IsChecked = _settings.LowRamMode;
            ConnectionCountComboBox.SelectedValue = _settings.ConnectionCount.ToString();
            if (!_settings.LegalUseAcknowledged)
            {
                MessageBox.Show(
                    this,
                    "請只下載你有權取得的直接檔案。HyperDL 不提供 DRM 繞過、受保護內容提取或存取限制繞過功能。",
                    "合法使用提醒",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                _settings = _settings with { LegalUseAcknowledged = true };
                await UserSettingsStore.SaveAsync(_settings);
            }
            _downloadHistory.AddRange(await _downloadDatabase.LoadSessionsAsync());
            foreach (var session in _downloadHistory)
            {
                _downloadRows.Add(CreateListEntry(session));
            }
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "Status: 無法載入下載歷史。";
            MessageBox.Show(
                this,
                $"無法開啟下載資料庫：{exception.Message}",
                "資料庫錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(UrlTextBox.Text.Trim(), UriKind.Absolute, out var uri) ||
            !DownloadUrlValidator.IsSupported(uri))
        {
            StatusTextBlock.Text = "請輸入有效的 HTTP 或 HTTPS 網址。";
            MessageBox.Show(
                this,
                "網址格式不正確，請輸入直接下載檔案的 HTTP 或 HTTPS 網址。",
                "無效網址",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(
                ConnectionCountComboBox.SelectedValue?.ToString(),
                out var requestedConnectionCount))
        {
            StatusTextBlock.Text = "連線數設定無效。";
            return;
        }
        StartDownloadButton.IsEnabled = false;
        CancelDownloadButton.IsEnabled = true;
        var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        DownloadProgressBar.Value = 0;
        DownloadProgressBar.IsIndeterminate = false;
        PercentageTextBlock.Text = "0%";
        FileTextBlock.Text = "File: Preparing...";
        ServerTextBlock.Text = "伺服器：探測中...";
        FileSizeTextBlock.Text = "檔案大小：探測中...";
        RangeSupportTextBlock.Text = "Range：探測中...";
        DownloadModeTextBlock.Text = "下載模式：單連線下載";
        DownloadedTextBlock.Text = "Downloaded: 0 B / checking total size...";
        SpeedTextBlock.Text = "Speed: 0 B/s";
        StatusTextBlock.Text = "Status: Checking server range support...";

        try
        {
            var metadata = await _httpProbe.ProbeAsync(uri, cancellation.Token);
            FileTextBlock.Text = $"File: {metadata.FileName}";
            ServerTextBlock.Text = metadata.StatusCode.HasValue
                ? $"伺服器：{metadata.FinalUri.Host} (HTTP {(int)metadata.StatusCode.Value})"
                : $"伺服器：{metadata.FinalUri.Host} (探測結果未知)";
            FileSizeTextBlock.Text = metadata.ContentLength.HasValue
                ? $"檔案大小：{FormatBytes(metadata.ContentLength.Value)}"
                : "檔案大小：未知";
            RangeSupportTextBlock.Text = $"Range：{GetRangeSupportText(metadata.RangeSupport)}";
            var segmentCount = DownloadEngine.GetSegmentCount(metadata, requestedConnectionCount);
            DownloadModeTextBlock.Text = segmentCount > 1
                ? $"下載模式：{segmentCount} 段分段下載"
                : "下載模式：單連線下載";
            SegmentStatusTextBlock.Text = segmentCount > 1
                ? $"Segments：{segmentCount} 段等待開始"
                : "Segment：單連線模式";
            StatusTextBlock.Text = "Status: Server checked. Starting download...";
            StatusTextBlock.Text = "Status: Downloading...";
            _activeSession = metadata.RangeSupport == ByteRangeSupport.Supported &&
                             metadata.ContentLength is > 0
                ? _downloadEngine.CreateSession(metadata, requestedConnectionCount)
                : null;
            _activeEntry = _activeSession is null
                ? new DownloadListEntry(
                    Guid.NewGuid(),
                    metadata.FileName,
                    metadata.ContentLength.HasValue
                        ? FormatBytes(metadata.ContentLength.Value)
                        : "未知",
                    "0%",
                    "Downloading",
                    1,
                    string.Empty,
                    null)
                : CreateListEntry(_activeSession);
            _downloadRows.Insert(0, _activeEntry);
            if (_activeSession is not null)
            {
                _activeSession.Status = DownloadSessionStatus.Downloading;
                _downloadHistory.Add(_activeSession);
                await _downloadDatabase.SaveSessionAsync(_activeSession, cancellation.Token);
            }
            _activeEntry.Status = "Downloading";
            _activeEntry.ConnectionCount = segmentCount;
            PauseDownloadButton.IsEnabled = _activeSession is not null;
            var outputPath = _activeSession is null
                ? await _downloadEngine.DownloadAsync(
                    metadata,
                    CreateProgressReporter(_activeEntry),
                    cancellation.Token,
                    requestedConnectionCount)
                : await DownloadSessionWithCheckpointsAsync(
                    _activeSession,
                    CreateProgressReporter(_activeEntry),
                    cancellation.Token);
            if (_activeSession is not null)
            {
                await _downloadDatabase.SaveSessionAsync(_activeSession);
            }
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = 100;
            PercentageTextBlock.Text = "100%";
            FileTextBlock.Text = $"File: {Path.GetFileName(outputPath)}";
            StatusTextBlock.Text = $"Status: Download complete - {outputPath}";
            _activeEntry.Status = "Completed";
            _activeEntry.Progress = "100%";
            _activeEntry.Speed = "0 B/s";
            _activeEntry.Eta = "—";
            _activeEntry.TargetPath = outputPath;
            ResumeDownloadButton.IsEnabled = false;
        }
        catch (OperationCanceledException)
        {
            DownloadProgressBar.IsIndeterminate = false;
            if (_activeSession?.Status == DownloadSessionStatus.Paused)
            {
                await _downloadDatabase.SaveSessionAsync(_activeSession);
                StatusTextBlock.Text = "Status: Paused. Press Continue to resume.";
                if (_activeEntry is not null)
                {
                    _activeEntry.Status = "Paused";
                }
                ResumeDownloadButton.IsEnabled = true;
            }
            else
            {
                if (_activeSession is not null)
                {
                    await _downloadDatabase.SaveSessionAsync(_activeSession);
                }
                StatusTextBlock.Text = "Status: Download cancelled.";
                if (_activeEntry is not null)
                {
                    _activeEntry.Status = "Cancelled";
                }
                ResumeDownloadButton.IsEnabled = false;
            }
        }
        catch (Exception exception)
        {
            if (_activeSession is not null)
            {
                _activeSession.Status = DownloadSessionStatus.Failed;
                await _downloadDatabase.SaveSessionAsync(_activeSession);
            }
            if (_activeEntry is not null)
            {
                _activeEntry.Status = "Failed";
            }

            DownloadProgressBar.IsIndeterminate = false;
            StatusTextBlock.Text = "Status: Download failed.";
            MessageBox.Show(
                this,
                $"Download failed: {exception.Message}",
                "Download error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _downloadCancellation = null;
            cancellation.Dispose();
            CancelDownloadButton.IsEnabled = false;
            PauseDownloadButton.IsEnabled = false;
            StartDownloadButton.IsEnabled = _activeSession?.Status != DownloadSessionStatus.Paused;
        }
    }

    private IProgress<DownloadProgress> CreateProgressReporter(DownloadListEntry listEntry) =>
        new Progress<DownloadProgress>(update =>
        {
            FileTextBlock.Text = $"File: {update.FileName}";
            DownloadedTextBlock.Text = update.TotalBytes.HasValue
                ? $"Downloaded: {FormatBytes(update.BytesDownloaded)} / {FormatBytes(update.TotalBytes.Value)}"
                : $"Downloaded: {FormatBytes(update.BytesDownloaded)} / total size unknown";
            SpeedTextBlock.Text = $"Speed: {FormatBytes(update.BytesPerSecond)}/s";
            DownloadModeTextBlock.Text = update.SegmentCount > 1
                ? $"下載模式：{update.SegmentCount} 段分段下載（活動連線：{update.ActiveConnectionCount}）"
                : "下載模式：單連線下載";
            if (update.Segments is { Count: > 0 } segments)
            {
                SegmentStatusTextBlock.Text = string.Join(
                    Environment.NewLine,
                    segments.Select(segment =>
                        $"Segment {segment.Index}：{segment.Status} " +
                        $"({FormatBytes(segment.BytesDownloaded)} / {FormatBytes(segment.ExpectedLength)})"));
            }

            if (update.Percentage.HasValue)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = update.Percentage.Value;
                PercentageTextBlock.Text = $"{update.Percentage.Value:0.#}%";
            }
            else
            {
                DownloadProgressBar.IsIndeterminate = true;
                PercentageTextBlock.Text = "Total size unknown";
            }

            listEntry.Size = update.TotalBytes.HasValue
                ? FormatBytes(update.TotalBytes.Value)
                : "未知";
            listEntry.Progress = update.Percentage.HasValue
                ? $"{update.Percentage.Value:0.#}%"
                : FormatBytes(update.BytesDownloaded);
            listEntry.Speed = $"{FormatBytes(update.BytesPerSecond)}/s";
            listEntry.ConnectionCount = update.SegmentCount;
            listEntry.Status = "Downloading";
            listEntry.Eta = update.TotalBytes.HasValue && update.BytesPerSecond > 0
                ? FormatDuration(TimeSpan.FromSeconds(
                    Math.Max(0, update.TotalBytes.Value - update.BytesDownloaded) /
                    update.BytesPerSecond))
                : "—";
        });

    private async void ResumeDownload_Click(object sender, RoutedEventArgs e)
    {
        var session = (DownloadListDataGrid.SelectedItem as DownloadListEntry)?.Session ??
                      _activeSession;
        if (session is null ||
            session.Status is not (DownloadSessionStatus.Paused or DownloadSessionStatus.Recoverable))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        _activeSession = session;
        _activeEntry = DownloadListDataGrid.SelectedItem as DownloadListEntry ??
                       _downloadRows.FirstOrDefault(entry => entry.Id == session.Id);
        ResumeDownloadButton.IsEnabled = false;
        StartDownloadButton.IsEnabled = false;
        CancelDownloadButton.IsEnabled = true;
        PauseDownloadButton.IsEnabled = true;
        try
        {
            StatusTextBlock.Text = "Status: Rechecking source before resume...";
            var currentMetadata = await _httpProbe.ProbeAsync(
                session.Metadata.FinalUri,
                cancellation.Token);
            session.EnsureSourceUnchanged(currentMetadata);
            if (currentMetadata.RangeSupport != ByteRangeSupport.Supported ||
                currentMetadata.ContentLength != session.Metadata.ContentLength)
            {
                throw new InvalidOperationException(
                    "無法確認來源仍支援相同的 Range 與檔案大小，已停止續傳。");
            }

            StatusTextBlock.Text = "Status: Resuming download...";
            session.Status = DownloadSessionStatus.Downloading;
            await _downloadDatabase.SaveSessionAsync(session, cancellation.Token);
            var outputPath = await DownloadSessionWithCheckpointsAsync(
                session,
                _activeEntry is null ? CreateProgressReporter(CreateListEntry(session)) : CreateProgressReporter(_activeEntry),
                cancellation.Token);
            await _downloadDatabase.SaveSessionAsync(session);
            FileTextBlock.Text = $"File: {Path.GetFileName(outputPath)}";
            StatusTextBlock.Text = $"Status: Download complete - {outputPath}";
            if (_activeEntry is not null)
            {
                _activeEntry.Status = "Completed";
                _activeEntry.Progress = "100%";
                _activeEntry.TargetPath = outputPath;
            }
        }
        catch (OperationCanceledException)
        {
            await _downloadDatabase.SaveSessionAsync(session);
            StatusTextBlock.Text = session.Status == DownloadSessionStatus.Paused
                ? "Status: Paused. Press Continue to resume."
                : "Status: Download cancelled.";
            if (_activeEntry is not null)
            {
                _activeEntry.Status = session.Status.ToString();
            }
            ResumeDownloadButton.IsEnabled = session.Status == DownloadSessionStatus.Paused;
        }
        catch (Exception exception)
        {
            session.Status = DownloadSessionStatus.Failed;
            await _downloadDatabase.SaveSessionAsync(session);
            if (_activeEntry is not null)
            {
                _activeEntry.Status = "Failed";
            }
            StatusTextBlock.Text = "Status: Download failed.";
            MessageBox.Show(
                this,
                $"Download failed: {exception.Message}",
                "Download error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _downloadCancellation = null;
            cancellation.Dispose();
            CancelDownloadButton.IsEnabled = false;
            PauseDownloadButton.IsEnabled = false;
            StartDownloadButton.IsEnabled = session.Status != DownloadSessionStatus.Paused;
        }
    }

    private async Task<string> DownloadSessionWithCheckpointsAsync(
        DownloadSession session,
        IProgress<DownloadProgress> progress,
        CancellationToken cancellationToken)
    {
        using var checkpointCancellation = new CancellationTokenSource();
        var downloadTask = _downloadEngine.DownloadAsync(session, progress, cancellationToken);
        var checkpointTask = CheckpointSessionAsync(session, checkpointCancellation.Token);
        try
        {
            var firstTask = await Task.WhenAny(downloadTask, checkpointTask);
            if (firstTask == checkpointTask)
            {
                await checkpointTask;
            }

            return await downloadTask;
        }
        catch
        {
            session.Pause();
            try
            {
                await downloadTask;
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }
        finally
        {
            checkpointCancellation.Cancel();
            try
            {
                await checkpointTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task CheckpointSessionAsync(
        DownloadSession session,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await _downloadDatabase.SaveSessionAsync(session, cancellationToken);
        }
    }

    private void PauseDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSession is null)
        {
            StatusTextBlock.Text = "此下載模式不支援續傳；可取消後重新開始。";
            return;
        }

        PauseDownloadButton.IsEnabled = false;
        ResumeDownloadButton.IsEnabled = false;
        StatusTextBlock.Text = "Status: Pausing...";
        _activeSession.Pause();
    }

    private async void CancelDownload_Click(object sender, RoutedEventArgs e)
    {
        CancelDownloadButton.IsEnabled = false;
        StatusTextBlock.Text = "Status: Cancelling download...";
        if (_activeSession?.Status == DownloadSessionStatus.Paused)
        {
            if (File.Exists(_activeSession.PartPath))
            {
                File.Delete(_activeSession.PartPath);
            }

            _activeSession.Status = DownloadSessionStatus.Cancelled;
            await _downloadDatabase.SaveSessionAsync(_activeSession);
            var entry = _downloadRows.FirstOrDefault(item => item.Id == _activeSession.Id);
            if (entry is not null)
            {
                entry.Status = "Cancelled";
            }
            _activeSession = null;
            ResumeDownloadButton.IsEnabled = false;
            StartDownloadButton.IsEnabled = true;
            StatusTextBlock.Text = "Status: Download cancelled.";
            return;
        }

        _downloadCancellation?.Cancel();
    }

    private DownloadListEntry CreateListEntry(DownloadSession session)
    {
        var downloaded = session.Segments.Sum(segment => segment.BytesDownloaded);
        var total = session.Metadata.ContentLength!.Value;
        var percentage = total > 0 ? downloaded * 100d / total : 0;
        return new DownloadListEntry(
            session.Id,
            session.FileName,
            FormatBytes(total),
            session.Status == DownloadSessionStatus.Completed ? "100%" : $"{percentage:0.#}%",
            session.Status.ToString(),
            session.ConnectionCount,
            session.FinalPath,
            session);
    }

    private void DownloadList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var entry = DownloadListDataGrid.SelectedItem as DownloadListEntry;
        var session = entry?.Session;
        ResumeDownloadButton.IsEnabled = _downloadCancellation is null &&
            session?.Status is DownloadSessionStatus.Paused or DownloadSessionStatus.Recoverable;
        PauseDownloadButton.IsEnabled = _downloadCancellation is not null &&
            ReferenceEquals(session, _activeSession) &&
            session?.Status == DownloadSessionStatus.Downloading;
        CancelDownloadButton.IsEnabled = session?.Status == DownloadSessionStatus.Paused &&
            _downloadCancellation is null;
    }

    private async void DeleteRecord_Click(object sender, RoutedEventArgs e)
    {
        if (DownloadListDataGrid.SelectedItem is not DownloadListEntry entry)
        {
            return;
        }

        if (entry.Session is { } session)
        {
            if (File.Exists(session.PartPath))
            {
                var removePartial = MessageBox.Show(
                    this,
                    "此紀錄有未完成的 .part 檔案。是否一併刪除該暫存檔？",
                    "刪除下載紀錄",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (removePartial == MessageBoxResult.Cancel)
                {
                    return;
                }

                if (removePartial == MessageBoxResult.Yes)
                {
                    File.Delete(session.PartPath);
                }
            }

            await _downloadDatabase.DeleteSessionAsync(session.Id);
            _downloadHistory.Remove(session);
        }

        _downloadRows.Remove(entry);
        if (ReferenceEquals(_activeEntry, entry))
        {
            _activeEntry = null;
            _activeSession = null;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var entry = DownloadListDataGrid.SelectedItem as DownloadListEntry;
        var path = entry?.TargetPath;
        var folder = !string.IsNullOrWhiteSpace(path)
            ? Path.GetDirectoryName(path)
            : _settings.DownloadDirectory;
        if (string.IsNullOrWhiteSpace(folder))
        {
            folder = _settings.DownloadDirectory;
        }

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private void ToggleSettings_Click(object sender, RoutedEventArgs e) =>
        SettingsExpander.IsExpanded = !SettingsExpander.IsExpanded;

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MaximumConcurrentTextBox.Text, out var maximumConcurrent) ||
            maximumConcurrent is < 1 or > 64 ||
            !int.TryParse(GlobalConnectionLimitTextBox.Text, out var globalConnectionLimit) ||
            globalConnectionLimit is < 1 or > DownloadEngine.MaximumSegmentCount ||
            !int.TryParse(ConnectionCountComboBox.SelectedValue?.ToString(), out var connections) ||
            string.IsNullOrWhiteSpace(SettingsDownloadDirectoryTextBox.Text))
        {
            StatusTextBlock.Text = "設定無效：請確認下載路徑、連線數與同時下載數。";
            return;
        }

        try
        {
            var settings = new HyperDLSettings
            {
                DownloadDirectory = Path.GetFullPath(SettingsDownloadDirectoryTextBox.Text.Trim()),
                ConnectionCount = connections,
                MaximumConcurrentDownloads = maximumConcurrent,
                GlobalConnectionLimit = globalConnectionLimit,
                LowRamMode = LowRamModeCheckBox.IsChecked == true,
                LegalUseAcknowledged = _settings.LegalUseAcknowledged
            };
            await UserSettingsStore.SaveAsync(settings);
            _settings = settings;
            GlobalConnectionLimiter.Shared.SetLimit(settings.GlobalConnectionLimit);
            _downloadEngine = new DownloadEngine(
                downloadDirectory: settings.DownloadDirectory,
                lowRamMode: settings.LowRamMode);
            StatusTextBlock.Text = "設定已儲存。佇列工作目前依序執行。";
        }
        catch (Exception exception)
        {
            StatusTextBlock.Text = "無法儲存設定。";
            MessageBox.Show(
                this,
                exception.Message,
                "設定錯誤",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{duration.Minutes}m {duration.Seconds}s";
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static string GetRangeSupportText(ByteRangeSupport support) =>
        support switch
        {
            ByteRangeSupport.Supported => "支援",
            ByteRangeSupport.Unsupported => "不支援",
            _ => "未知"
        };

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        base.OnClosed(e);
    }
}
