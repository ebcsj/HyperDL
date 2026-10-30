using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Markup;
using HyperDL.Core;

namespace HyperDL.Localization;

/// <summary>
/// Lightweight runtime localization. The indexer raises a single
/// <c>Item[]</c> notification, so every bound string updates when the
/// language changes without restarting the window.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, string> _table = StringTables.English;

    private LocalizationManager()
    {
    }

    public static LocalizationManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? LanguageChanged;

    public AppLanguage CurrentLanguage { get; private set; } = AppLanguage.System;

    public string this[string key] => StringTables.Resolve(_table, key);

    public string Get(string key) => this[key];

    public void Apply(AppLanguage language)
    {
        CurrentLanguage = language;
        _table = language switch
        {
            AppLanguage.TraditionalChinese => StringTables.TraditionalChinese,
            AppLanguage.English => StringTables.English,
            _ => StringTables.ForCulture(CultureInfo.CurrentUICulture)
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public static string GetString(string key) => Instance[key];
}

/// <summary>
/// Markup extension that binds a XAML property to the live localization table:
/// <c>Text="{loc:Loc AddButton}"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key
        };
        return binding.ProvideValue(serviceProvider);
    }
}

public static class StringTables
{
    private static readonly ReadOnlyDictionary<string, string> EnglishTable =
        new(BuildEnglish());
    private static readonly ReadOnlyDictionary<string, string> ChineseTable =
        new(BuildTraditionalChinese());

    public static IReadOnlyDictionary<string, string> English => EnglishTable;
    public static IReadOnlyDictionary<string, string> TraditionalChinese => ChineseTable;

    public static IReadOnlyDictionary<string, string> ForCulture(CultureInfo culture)
    {
        if (culture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ChineseTable;
        }

        return EnglishTable;
    }

    internal static string Resolve(IReadOnlyDictionary<string, string> table, string key)
    {
        if (table.TryGetValue(key, out var value))
        {
            return value;
        }

        return EnglishTable.TryGetValue(key, out value) ? value : key;
    }

    private static Dictionary<string, string> BuildEnglish() => new(StringComparer.Ordinal)
    {
        ["AppTitle"] = "HyperDL",
        ["AppSubtitle"] = "Fast, reliable downloads with dynamic segmentation",
        ["AddUrlWatermark"] = "Paste a direct HTTP/HTTPS download URL and press Enter",
        ["AddButton"] = "Add download",
        ["PauseButton"] = "Pause",
        ["ResumeButton"] = "Resume",
        ["CancelButton"] = "Cancel",
        ["RetryButton"] = "Retry",
        ["RemoveButton"] = "Remove",
        ["OpenFileButton"] = "Open file",
        ["OpenFolderButton"] = "Open folder",
        ["CopyDetailsButton"] = "Copy details",
        ["CopyUrlButton"] = "Copy URL",
        ["EditUrlButton"] = "Edit URL…",
        ["DeleteFileButton"] = "Delete file and remove",
        ["ClearCompletedButton"] = "Clear completed",
        ["OpenLogsButton"] = "Open logs",
        ["SearchWatermark"] = "Filter by name, URL, or status",
        ["SpeedLimitLabel"] = "Speed limit (KiB/s, 0 = unlimited)",
        ["SpeedLimitHint"] = "Applied to the total of all active connections.",
        ["SettingsButton"] = "Settings",
        ["CloseSettingsButton"] = "Close",
        ["SaveSettingsButton"] = "Save settings",
        ["BrowseButton"] = "Browse…",
        ["RegisterBrowserButton"] = "Register browser integration",
        ["ColumnFile"] = "File",
        ["ColumnSize"] = "Size",
        ["ColumnProgress"] = "Progress",
        ["ColumnSpeed"] = "Speed",
        ["ColumnEta"] = "ETA",
        ["ColumnConnections"] = "Connections",
        ["ColumnStatus"] = "Status",
        ["ColumnBuffer"] = "Buffer",
        ["DetailsTitle"] = "Selection details",
        ["DetailsEmpty"] = "Select a download to see its details.",
        ["SourceLabel"] = "Source",
        ["TargetLabel"] = "Saved to",
        ["ConnectionsDetailLabel"] = "Connections",
        ["SegmentsLabel"] = "Segments",
        ["ErrorLabel"] = "Last error",
        ["ReadyStatus"] = "Ready",
        ["ActiveDownloadsFormat"] = "{0} active",
        ["TotalSpeedFormat"] = "{0}/s",
        ["GlobalConnectionsFormat"] = "{0} connections",
        ["SettingsTitle"] = "Settings",
        ["DownloadFolderLabel"] = "Download folder",
        ["ConnectionsLabel"] = "Connections per download",
        ["ConnectionsAuto"] = "Auto",
        ["ConcurrentLabel"] = "Concurrent downloads",
        ["BufferModeLabel"] = "Buffer mode",
        ["DynamicSegmentationLabel"] = "Dynamic segmentation",
        ["DynamicSegmentationHint"] = "Let idle connections split large files while downloading (64 MB minimum).",
        ["AutoResumeLabel"] = "Resume unfinished downloads on startup",
        ["LanguageLabel"] = "Language",
        ["PerHostLabel"] = "Max connections per host (0 = unlimited)",
        ["ConfirmExternalLabel"] = "Confirm downloads opened by the browser extension",
        ["LegalTitle"] = "Please download responsibly",
        ["LegalMessage"] = "Only download files you have the right to access. HyperDL does not bypass DRM, protected content, or access restrictions.",
        ["LegalAcceptButton"] = "I understand",
        ["StatusQueued"] = "Queued",
        ["StatusDownloading"] = "Downloading",
        ["StatusPausing"] = "Pausing…",
        ["StatusPaused"] = "Paused",
        ["StatusRecoverable"] = "Interrupted",
        ["StatusCompleted"] = "Completed",
        ["StatusCancelled"] = "Cancelled",
        ["StatusFailed"] = "Failed",
        ["StatusUnknown"] = "Unknown",
        ["BufferAuto"] = "Auto",
        ["BufferLowRam"] = "Low RAM",
        ["BufferBalanced"] = "Balanced",
        ["BufferFast"] = "Fast",
        ["LanguageSystem"] = "System default",
        ["LanguageEnglish"] = "English",
        ["LanguageTraditionalChinese"] = "繁體中文（台灣）",
        ["ErrorNetwork"] = "The connection was interrupted. Check your network and retry.",
        ["ErrorTimeout"] = "The server stopped responding.",
        ["ErrorNotFound"] = "The file was not found (HTTP 404).",
        ["ErrorForbidden"] = "The server refused access (HTTP 401/403).",
        ["ErrorRateLimited"] = "The server is rate limiting requests. Wait a moment and retry.",
        ["ErrorServer"] = "The server reported an error.",
        ["ErrorDiskFull"] = "There is not enough free disk space.",
        ["ErrorFileSystemLimit"] = "The destination drive cannot store a file this large. Use an NTFS or exFAT folder.",
        ["ErrorPermission"] = "Windows denied access to the destination folder.",
        ["ErrorSourceChanged"] = "The file changed on the server. Remove it and download again.",
        ["ErrorInvalidData"] = "The server sent an unexpected response. Retry or use another mirror.",
        ["ErrorCancelled"] = "The download was cancelled.",
        ["ErrorUnknown"] = "The download failed.",
        ["ErrorDetailFormat"] = "{0}\n\nTechnical details: {1}",
        ["InvalidUrlTitle"] = "Invalid URL",
        ["InvalidUrlMessage"] = "Enter a direct HTTP or HTTPS download URL.",
        ["NoSelectionTitle"] = "Nothing selected",
        ["NoSelectionMessage"] = "Select a download first.",
        ["ConfirmRemoveTitle"] = "Remove download",
        ["ConfirmRemoveMessage"] = "Remove \"{0}\" from the list? The partial file will be kept.",
        ["ConfirmRemoveMultipleMessage"] = "Remove {0} downloads from the list? Partial files will be kept.",
        ["ConfirmDeleteFileTitle"] = "Delete downloaded file",
        ["ConfirmDeleteFileMessage"] = "Remove \"{0}\" and delete every file it downloaded (including partial data)? This cannot be undone.",
        ["ConfirmDeleteMultipleFilesMessage"] = "Remove {0} downloads and delete their files, including partial data? This cannot be undone.",
        ["ConfirmClearCompletedTitle"] = "Clear completed downloads",
        ["ConfirmClearCompletedMessage"] = "Remove {0} completed entries from the list? Downloaded files will not be deleted.",
        ["ClearCompletedFormat"] = "Cleared {0} completed entries.",
        ["DeleteFileFormat"] = "Deleted {0} file(s).",
        ["DeleteFileErrorFormat"] = "Could not delete the file:\n{0}",
        ["EditUrlTitle"] = "Edit download URL",
        ["EditUrlLabel"] = "New URL",
        ["EditUrlUnavailableMessage"] = "This download has already started. Copy its URL instead, or remove it and add the new link.",
        ["EditUrlDuplicateMessage"] = "That URL is already in the queue.",
        ["DatabaseErrorTitle"] = "History unavailable",
        ["DatabaseErrorFormat"] = "HyperDL could not open its download history:\n{0}",
        ["SettingsSavedTitle"] = "Settings saved",
        ["SettingsSavedMessage"] = "Connection and buffer changes apply to new downloads.",
        ["SettingsLoadErrorTitle"] = "Settings not loaded",
        ["SettingsLoadErrorFormat"] = "HyperDL is using default settings because the saved settings could not be read:\n{0}",
        ["BrowserRegisteredTitle"] = "Browser integration",
        ["BrowserRegisteredMessage"] = "The hyperdl:// link is now registered. Reload the Chrome extension and try again.",
        ["BrowserRegisterFailedFormat"] = "Could not register the hyperdl:// link:\n{0}",
        ["ExternalDownloadTitle"] = "Open download from browser?",
        ["ExternalDownloadMessage"] = "A browser extension asked HyperDL to download:\n\n{0}",
        ["DownloadCompleteTitle"] = "Download complete",
        ["DownloadCompleteFormat"] = "\"{0}\" finished downloading.",
        ["OpenFileFailedFormat"] = "Could not open the file:\n{0}",
        ["FolderMissingMessage"] = "The folder does not exist yet.",
        ["LogsMissingMessage"] = "The logs folder has not been created yet.",
        ["BatchAddedFormat"] = "Added {0} downloads.",
        ["DuplicateSkippedFormat"] = "Skipped {0} duplicate or queued URL(s).",
        ["ClipboardEmpty"] = "The clipboard does not contain a supported URL.",
        ["ConfirmRemoveCompletedMessage"] = "Remove \"{0}\" from the list? The downloaded file will not be deleted.",
        ["DetailsCopied"] = "Details copied to the clipboard.",
        ["WindowTitleFormat"] = "HyperDL — {0}",
        ["PauseAllButton"] = "Pause all",
        ["ResumeAllButton"] = "Resume all",
        ["ToolTipAdd"] = "Add the URL in the box (Enter)",
        ["ToolTipSettings"] = "Change download, buffer, and language settings",
        ["AboutText"] = "HyperDL is an open-source download manager. Only download content you are allowed to access.",
        ["NoDownloadsYet"] = "No downloads yet. Paste a URL above to get started.",
    };

    private static Dictionary<string, string> BuildTraditionalChinese() => new(StringComparer.Ordinal)
    {
        ["AppTitle"] = "HyperDL",
        ["AppSubtitle"] = "動態分段，快速又可靠的下載管理器",
        ["AddUrlWatermark"] = "貼上 HTTP/HTTPS 直接下載網址，然後按 Enter",
        ["AddButton"] = "加入下載",
        ["PauseButton"] = "暫停",
        ["ResumeButton"] = "繼續",
        ["CancelButton"] = "取消",
        ["RetryButton"] = "重試",
        ["RemoveButton"] = "移除",
        ["OpenFileButton"] = "開啟檔案",
        ["OpenFolderButton"] = "開啟資料夾",
        ["CopyDetailsButton"] = "複製詳細資料",
        ["CopyUrlButton"] = "複製網址",
        ["EditUrlButton"] = "編輯網址…",
        ["DeleteFileButton"] = "刪除檔案並移除",
        ["ClearCompletedButton"] = "清除已完成",
        ["OpenLogsButton"] = "開啟記錄檔",
        ["SearchWatermark"] = "依檔名、網址或狀態篩選",
        ["SpeedLimitLabel"] = "速度上限（KiB/秒，0 = 無限制）",
        ["SpeedLimitHint"] = "套用至所有使用中連線的總和。",
        ["SettingsButton"] = "設定",
        ["CloseSettingsButton"] = "關閉",
        ["SaveSettingsButton"] = "儲存設定",
        ["BrowseButton"] = "瀏覽…",
        ["RegisterBrowserButton"] = "註冊瀏覽器整合",
        ["ColumnFile"] = "檔案",
        ["ColumnSize"] = "大小",
        ["ColumnProgress"] = "進度",
        ["ColumnSpeed"] = "速度",
        ["ColumnEta"] = "剩餘時間",
        ["ColumnConnections"] = "連線數",
        ["ColumnStatus"] = "狀態",
        ["ColumnBuffer"] = "緩衝區",
        ["DetailsTitle"] = "選取項目詳細資料",
        ["DetailsEmpty"] = "選取一筆下載以查看詳細資料。",
        ["SourceLabel"] = "來源",
        ["TargetLabel"] = "儲存位置",
        ["ConnectionsDetailLabel"] = "連線數",
        ["SegmentsLabel"] = "分段數",
        ["ErrorLabel"] = "最近錯誤",
        ["ReadyStatus"] = "就緒",
        ["ActiveDownloadsFormat"] = "{0} 個進行中",
        ["TotalSpeedFormat"] = "{0}/秒",
        ["GlobalConnectionsFormat"] = "{0} 條連線",
        ["SettingsTitle"] = "設定",
        ["DownloadFolderLabel"] = "下載資料夾",
        ["ConnectionsLabel"] = "每筆下載連線數",
        ["ConnectionsAuto"] = "自動",
        ["ConcurrentLabel"] = "同時下載數量",
        ["BufferModeLabel"] = "緩衝區模式",
        ["DynamicSegmentationLabel"] = "動態分段",
        ["DynamicSegmentationHint"] = "下載時讓閒置連線分割大檔案（至少 64 MB）。",
        ["AutoResumeLabel"] = "啟動時自動續傳未完成的下載",
        ["LanguageLabel"] = "語言",
        ["PerHostLabel"] = "每個主機最大連線數（0 = 無限制）",
        ["ConfirmExternalLabel"] = "瀏覽器擴充功能開啟下載前先確認",
        ["LegalTitle"] = "請合法使用",
        ["LegalMessage"] = "請只下載你有權取得的檔案。HyperDL 不提供 DRM 繞過、受保護內容提取或存取限制繞過功能。",
        ["LegalAcceptButton"] = "我了解",
        ["StatusQueued"] = "等待中",
        ["StatusDownloading"] = "下載中",
        ["StatusPausing"] = "暫停中…",
        ["StatusPaused"] = "已暫停",
        ["StatusRecoverable"] = "已中斷",
        ["StatusCompleted"] = "已完成",
        ["StatusCancelled"] = "已取消",
        ["StatusFailed"] = "失敗",
        ["StatusUnknown"] = "未知",
        ["BufferAuto"] = "自動",
        ["BufferLowRam"] = "低記憶體",
        ["BufferBalanced"] = "平衡",
        ["BufferFast"] = "快速",
        ["LanguageSystem"] = "跟隨系統",
        ["LanguageEnglish"] = "English",
        ["LanguageTraditionalChinese"] = "繁體中文（台灣）",
        ["ErrorNetwork"] = "連線中斷，請檢查網路後重試。",
        ["ErrorTimeout"] = "伺服器沒有回應。",
        ["ErrorNotFound"] = "找不到檔案（HTTP 404）。",
        ["ErrorForbidden"] = "伺服器拒絕存取（HTTP 401/403）。",
        ["ErrorRateLimited"] = "伺服器正在限制請求速度，請稍後重試。",
        ["ErrorServer"] = "伺服器回報錯誤。",
        ["ErrorDiskFull"] = "磁碟可用空間不足。",
        ["ErrorFileSystemLimit"] = "目的地磁碟無法儲存這麼大的檔案，請改用 NTFS 或 exFAT 資料夾。",
        ["ErrorPermission"] = "Windows 拒絕存取目的地資料夾。",
        ["ErrorSourceChanged"] = "伺服器上的檔案已變更，請移除後重新下載。",
        ["ErrorInvalidData"] = "伺服器回應格式不正確，請重試或改用其他來源。",
        ["ErrorCancelled"] = "下載已取消。",
        ["ErrorUnknown"] = "下載失敗。",
        ["ErrorDetailFormat"] = "{0}\n\n技術詳細資料：{1}",
        ["InvalidUrlTitle"] = "網址無效",
        ["InvalidUrlMessage"] = "請輸入 HTTP 或 HTTPS 直接下載網址。",
        ["NoSelectionTitle"] = "尚未選取",
        ["NoSelectionMessage"] = "請先選取一筆下載。",
        ["ConfirmRemoveTitle"] = "移除下載",
        ["ConfirmRemoveMessage"] = "要從清單移除「{0}」嗎？暫存檔會保留。",
        ["ConfirmRemoveMultipleMessage"] = "要從清單移除 {0} 筆下載嗎？暫存檔會保留。",
        ["ConfirmDeleteFileTitle"] = "刪除已下載檔案",
        ["ConfirmDeleteFileMessage"] = "要移除「{0}」並刪除它下載的所有檔案（含暫存資料）嗎？此操作無法復原。",
        ["ConfirmDeleteMultipleFilesMessage"] = "要移除 {0} 筆下載並刪除其檔案（含暫存資料）嗎？此操作無法復原。",
        ["ConfirmClearCompletedTitle"] = "清除已完成下載",
        ["ConfirmClearCompletedMessage"] = "要從清單移除 {0} 筆已完成項目嗎？已下載的檔案不會刪除。",
        ["ClearCompletedFormat"] = "已清除 {0} 筆已完成項目。",
        ["DeleteFileFormat"] = "已刪除 {0} 個檔案。",
        ["DeleteFileErrorFormat"] = "無法刪除檔案：\n{0}",
        ["EditUrlTitle"] = "編輯下載網址",
        ["EditUrlLabel"] = "新網址",
        ["EditUrlUnavailableMessage"] = "這筆下載已經開始。請改為複製網址，或移除後重新加入新連結。",
        ["EditUrlDuplicateMessage"] = "這個網址已在佇列中。",
        ["DatabaseErrorTitle"] = "無法載入歷史紀錄",
        ["DatabaseErrorFormat"] = "HyperDL 無法開啟下載歷史紀錄：\n{0}",
        ["SettingsSavedTitle"] = "設定已儲存",
        ["SettingsSavedMessage"] = "連線與緩衝區變更會套用至新的下載。",
        ["SettingsLoadErrorTitle"] = "無法載入設定",
        ["SettingsLoadErrorFormat"] = "無法讀取已儲存的設定，HyperDL 將使用預設值：\n{0}",
        ["BrowserRegisteredTitle"] = "瀏覽器整合",
        ["BrowserRegisteredMessage"] = "已註冊 hyperdl:// 連結。請重新載入 Chrome 擴充功能再試一次。",
        ["BrowserRegisterFailedFormat"] = "無法註冊 hyperdl:// 連結：\n{0}",
        ["ExternalDownloadTitle"] = "要開啟瀏覽器下載嗎？",
        ["ExternalDownloadMessage"] = "瀏覽器擴充功能要求 HyperDL 下載：\n\n{0}",
        ["DownloadCompleteTitle"] = "下載完成",
        ["DownloadCompleteFormat"] = "「{0}」已完成下載。",
        ["OpenFileFailedFormat"] = "無法開啟檔案：\n{0}",
        ["FolderMissingMessage"] = "資料夾尚未建立。",
        ["LogsMissingMessage"] = "記錄檔資料夾尚未建立。",
        ["BatchAddedFormat"] = "已加入 {0} 筆下載。",
        ["DuplicateSkippedFormat"] = "已略過 {0} 個重複或已在佇列的網址。",
        ["ClipboardEmpty"] = "剪貼簿中沒有支援的網址。",
        ["ConfirmRemoveCompletedMessage"] = "要從清單移除「{0}」嗎？已下載的檔案不會刪除。",
        ["DetailsCopied"] = "詳細資料已複製到剪貼簿。",
        ["WindowTitleFormat"] = "HyperDL — {0}",
        ["PauseAllButton"] = "全部暫停",
        ["ResumeAllButton"] = "全部繼續",
        ["ToolTipAdd"] = "加入上方網址（Enter）",
        ["ToolTipSettings"] = "變更下載、緩衝區與語言設定",
        ["AboutText"] = "HyperDL 是開放原始碼下載管理器。請只下載你有權存取的內容。",
        ["NoDownloadsYet"] = "尚無下載。請在上方貼上網址開始使用。",
    };
}
