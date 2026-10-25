using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using Microsoft.Win32.SafeHandles;

namespace HyperDL.Core;

public sealed class DownloadEngine
{
    public const int MaximumSegmentCount = 64;
    public const long MinimumSegmentSize = 4 * 1024 * 1024;
    public const int LowRamBufferSize = 64 * 1024;
    public const int BalancedBufferSize = 80 * 1024;
    public const long GlobalNetworkBufferBudgetBytes = 64L * 1024 * 1024;
    private static readonly HttpClient Client = CreateHttpClient();
    private readonly HttpClient _client;
    private readonly string _downloadDirectory;
    private readonly int _bufferSize;
    private readonly GlobalConnectionLimiter _connectionLimiter;
    public int BufferSize => _bufferSize;

    public DownloadEngine(
        HttpClient? client = null,
        string? downloadDirectory = null,
        bool lowRamMode = false,
        GlobalConnectionLimiter? connectionLimiter = null)
    {
        _client = client ?? Client;
        _downloadDirectory = downloadDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        _bufferSize = lowRamMode ? LowRamBufferSize : BalancedBufferSize;
        _connectionLimiter = connectionLimiter ?? GlobalConnectionLimiter.Shared;
    }

    public static int GetSegmentCount(DownloadMetadata metadata, int requestedConnectionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (requestedConnectionCount is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedConnectionCount),
                "連線數只可選擇 Auto、1、2、4、8、16、32 或 64。");
        }

        if (!DownloadUrlValidator.IsSupported(metadata.FinalUri) ||
            metadata.RangeSupport != ByteRangeSupport.Supported ||
            metadata.ContentLength is not > 0)
        {
            return 1;
        }

        var possibleSegments = metadata.ContentLength.Value / MinimumSegmentSize;
        var desiredSegments = requestedConnectionCount == 0
            ? GetAutoSegmentCount(metadata.ContentLength.Value)
            : requestedConnectionCount;
        var usableSegments = Math.Min(desiredSegments, possibleSegments);
        return usableSegments >= 64 ? 64
            : usableSegments >= 32 ? 32
            : usableSegments >= 16 ? 16
            : usableSegments >= 8 ? 8
            : usableSegments >= 4 ? 4
            : usableSegments >= 2 ? 2
            : 1;
    }

    public Task<string> DownloadAsync(
        DownloadMetadata metadata,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        int requestedConnectionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (!DownloadUrlValidator.IsSupported(metadata.FinalUri))
        {
            throw new ArgumentException("請輸入有效的 HTTP 或 HTTPS 網址。", nameof(metadata));
        }

        var fileName = FileNameSanitizer.Sanitize(metadata.FileName);
        if (metadata.RangeSupport == ByteRangeSupport.Supported &&
            metadata.ContentLength is > 0)
        {
            return DownloadAsync(
                CreateSession(metadata, requestedConnectionCount),
                progress,
                cancellationToken);
        }

        return DownloadSingleAsync(metadata.FinalUri, progress, cancellationToken, fileName);
    }

    public DownloadSession CreateSession(DownloadMetadata metadata, int requestedConnectionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!DownloadUrlValidator.IsSupported(metadata.FinalUri) ||
            metadata.RangeSupport != ByteRangeSupport.Supported ||
            metadata.ContentLength is not > 0)
        {
            throw new ArgumentException(
                "需要已確認支援 Range 且大小已知的 HTTP/HTTPS 檔案才能建立可續傳工作。",
                nameof(metadata));
        }

        Directory.CreateDirectory(_downloadDirectory);
        var fileName = FileNameSanitizer.Sanitize(metadata.FileName);
        var connectionCount = GetSegmentCount(metadata, requestedConnectionCount);
        var ranges = RangeCalculator.Calculate(metadata.ContentLength.Value, connectionCount);
        var segments = ranges
            .Select((range, index) => new DownloadSegment(index + 1, range))
            .ToArray();
        var finalPath = GetAvailableFilePath(_downloadDirectory, fileName);
        return new DownloadSession(metadata, fileName, finalPath, connectionCount, segments);
    }

    public Task<string> DownloadAsync(
        DownloadSession session,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.PrepareResume();
        session.Status = DownloadSessionStatus.Downloading;
        return DownloadSegmentedAsync(session, progress, cancellationToken);
    }

    public async Task<string> DownloadAsync(
        Uri uri,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? suggestedFileName = null)
        => await DownloadSingleAsync(uri, progress, cancellationToken, suggestedFileName);

    private static int GetAutoSegmentCount(long fileSize)
    {
        if (fileSize < 8L * 1024 * 1024)
        {
            return 1;
        }

        if (fileSize < 32L * 1024 * 1024)
        {
            return 2;
        }

        if (fileSize < 128L * 1024 * 1024)
        {
            return 4;
        }

        if (fileSize < 1024L * 1024 * 1024)
        {
            return 8;
        }

        if (fileSize < 8L * 1024 * 1024 * 1024)
        {
            return 16;
        }

        if (fileSize < 32L * 1024 * 1024 * 1024)
        {
            return 32;
        }

        return 64;
    }

    private async Task<string> DownloadSingleAsync(
        Uri uri,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        string? suggestedFileName)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!DownloadUrlValidator.IsSupported(uri))
        {
            throw new ArgumentException("請輸入有效的 HTTP 或 HTTPS 網址。", nameof(uri));
        }

        Directory.CreateDirectory(_downloadDirectory);

        var fileName = FileNameSanitizer.Sanitize(
            string.IsNullOrWhiteSpace(suggestedFileName) ? GetFileName(uri) : suggestedFileName);
        var outputPath = GetAvailableFilePath(_downloadDirectory, fileName);
        var partPath = outputPath + ".part";
        var partCreated = false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri)
            {
                Version = HttpVersion.Version11,
                VersionPolicy = HttpVersionPolicy.RequestVersionExact
            };
            using var connectionLease = await _connectionLimiter.AcquireAsync(cancellationToken)
                .ConfigureAwait(false);
            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var totalBytes = response.Content.Headers.ContentLength;
            var buffer = new byte[_bufferSize];
            long bytesDownloaded = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReportTime = stopwatch.Elapsed;
            long lastReportedBytes = 0;

            await using (var fileStream = new FileStream(
                             partPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: _bufferSize,
                             options: FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                partCreated = true;
                ReportProgress(force: true);

                while (true)
                {
                    var bytesRead = await networkStream.ReadAsync(buffer, cancellationToken);
                    if (bytesRead == 0)
                    {
                        ReportProgress(force: true, isComplete: true);
                        break;
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    bytesDownloaded += bytesRead;

                    if (stopwatch.Elapsed - lastReportTime >= TimeSpan.FromMilliseconds(250))
                    {
                        ReportProgress();
                    }
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                fileStream.Flush(flushToDisk: true);
            }

            if (totalBytes.HasValue && bytesDownloaded != totalBytes.Value)
            {
                throw new InvalidDataException(
                    $"下載資料長度不符：預期 {totalBytes.Value} bytes，實際 {bytesDownloaded} bytes。");
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partPath, outputPath, overwrite: false);
            return outputPath;

            void ReportProgress(bool force = false, bool isComplete = false)
            {
                var elapsed = stopwatch.Elapsed - lastReportTime;
                if (!force && elapsed < TimeSpan.FromMilliseconds(250))
                {
                    return;
                }

                var bytesPerSecond = elapsed.TotalSeconds > 0
                    ? (bytesDownloaded - lastReportedBytes) / elapsed.TotalSeconds
                    : 0;
                var percentage = totalBytes.HasValue
                    ? totalBytes.Value == 0
                        ? isComplete ? 100 : 0
                        : Math.Clamp(bytesDownloaded * 100d / totalBytes.Value, 0, 100)
                    : (double?)null;

                progress?.Report(new DownloadProgress(
                    fileName,
                    bytesDownloaded,
                    totalBytes,
                    bytesPerSecond,
                    percentage));
                lastReportTime = stopwatch.Elapsed;
                lastReportedBytes = bytesDownloaded;
            }
        }
        catch
        {
            if (partCreated && File.Exists(partPath))
            {
                File.Delete(partPath);
            }

            throw;
        }
    }

    private async Task<string> DownloadSegmentedAsync(
        DownloadSession session,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = session.Metadata.FinalUri;
        var totalBytes = session.Metadata.ContentLength!.Value;
        var segments = session.Segments;
        var reporter = new DownloadProgressReporter(
            segments,
            progress,
            session.FileName,
            totalBytes,
            session.SyncRoot);
        var partPath = session.PartPath;
        var partCreated = File.Exists(partPath);

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                session.PauseToken);
            await using (var partStream = new FileStream(
                partPath,
                partCreated ? FileMode.Open : FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                _bufferSize,
                FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                partCreated = true;
                if (partStream.Length == 0)
                {
                    partStream.SetLength(totalBytes);
                }
                else if (partStream.Length != totalBytes)
                {
                    throw new InvalidDataException(
                        "暫存檔長度與來源檔案大小不符，無法安全繼續下載。");
                }

                foreach (var segment in segments.Where(
                             segment => segment.Status != DownloadSegmentStatus.Completed))
                {
                    segment.Status = DownloadSegmentStatus.Pending;
                }

                var safeFileHandle = partStream.SafeFileHandle;
                Exception? firstFailure = null;
                var workers = segments
                    .Where(segment => segment.Status != DownloadSegmentStatus.Completed)
                    .Select(segment => DownloadSegmentAsync(
                        uri,
                        totalBytes,
                        segment,
                        safeFileHandle,
                        reporter,
                        linkedCancellation,
                        exception =>
                        {
                            Interlocked.CompareExchange(ref firstFailure, exception, null);
                            linkedCancellation.Cancel();
                        }))
                    .ToArray();

                try
                {
                    await Task.WhenAll(workers).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    var failure = Volatile.Read(ref firstFailure);
                    if (failure is not null)
                    {
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancellationToken);
                    }

                    if (session.PauseRequested)
                    {
                        throw new OperationCanceledException(session.PauseToken);
                    }

                    ExceptionDispatchInfo.Capture(exception).Throw();
                    throw;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (segments.Any(segment =>
                        segment.Status != DownloadSegmentStatus.Completed ||
                        segment.BytesDownloaded != segment.ExpectedLength) ||
                    reporter.TotalDownloaded != totalBytes ||
                    partStream.Length != totalBytes)
                {
                    throw new InvalidDataException("分段下載的最終長度或狀態驗證失敗。");
                }

                await partStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                partStream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(partPath, session.FinalPath, overwrite: false);
            session.Status = DownloadSessionStatus.Completed;
            return session.FinalPath;
        }
        catch (OperationCanceledException) when (session.PauseRequested)
        {
            session.Status = DownloadSessionStatus.Paused;
            throw;
        }
        catch (OperationCanceledException)
        {
            session.Status = DownloadSessionStatus.Cancelled;
            if (partCreated && File.Exists(partPath))
            {
                File.Delete(partPath);
            }

            throw;
        }
        catch
        {
            session.Status = DownloadSessionStatus.Failed;
            if (partCreated && File.Exists(partPath))
            {
                File.Delete(partPath);
            }

            throw;
        }
    }

    private async Task DownloadSegmentAsync(
        Uri uri,
        long totalBytes,
        DownloadSegment segment,
        SafeFileHandle safeFileHandle,
        DownloadProgressReporter reporter,
        CancellationTokenSource linkedCancellation,
        Action<Exception> onFailure)
    {
        var cancellationToken = linkedCancellation.Token;

        try
        {
            if (segment.CurrentOffset > segment.EndOffset)
            {
                reporter.SetStatus(segment, DownloadSegmentStatus.Completed);
                return;
            }

            reporter.SetStatus(segment, DownloadSegmentStatus.Downloading);
            using var connectionLease = await _connectionLimiter.AcquireAsync(cancellationToken)
                .ConfigureAwait(false);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(segment.CurrentOffset, segment.EndOffset);

            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            ValidateSegmentResponse(
                response,
                segment.CurrentOffset,
                segment.EndOffset,
                totalBytes);
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var buffer = new byte[_bufferSize];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = segment.EndOffset - segment.CurrentOffset + 1;
                var readLength = (int)Math.Min(buffer.Length, remaining + 1);
                var bytesRead = await responseStream.ReadAsync(
                    buffer.AsMemory(0, readLength),
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                if (bytesRead > remaining)
                {
                    throw new InvalidDataException(
                        $"Segment {segment.Index} 回傳的資料超過要求長度。");
                }

                var fileOffset = segment.CurrentOffset;
                await RandomAccess.WriteAsync(
                    safeFileHandle,
                    buffer.AsMemory(0, bytesRead),
                    fileOffset,
                    cancellationToken).ConfigureAwait(false);
                reporter.RecordWritten(segment, bytesRead);
                reporter.Report();
            }

            if (segment.BytesDownloaded != segment.ExpectedLength)
            {
                throw new InvalidDataException(
                    $"Segment {segment.Index} 資料不足：預期 {segment.ExpectedLength} bytes，" +
                    $"實際 {segment.BytesDownloaded} bytes。");
            }

            reporter.SetStatus(segment, DownloadSegmentStatus.Completed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            reporter.SetStatus(segment, DownloadSegmentStatus.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            reporter.SetStatus(
                segment,
                DownloadSegmentStatus.Failed,
                exception.Message);
            onFailure(exception);
            throw;
        }
    }

    private static void ValidateSegmentResponse(
        HttpResponseMessage response,
        long expectedStart,
        long expectedEnd,
        long totalBytes)
    {
        var expectedLength = expectedEnd - expectedStart + 1;
        var contentRange = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent ||
            contentRange is null ||
            !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            contentRange.From != expectedStart ||
            contentRange.To != expectedEnd ||
            contentRange.To < contentRange.From ||
            contentRange.Length != totalBytes ||
            response.Content.Headers.ContentLength is long contentLength &&
            contentLength != expectedLength)
        {
            throw new InvalidDataException(
                $"HTTP Range {expectedStart}-{expectedEnd} 的狀態或 Content-Range 不符合要求。");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = MaximumSegmentCount
        };

        return new HttpClient(handler);
    }

    private static string GetFileName(Uri uri)
    {
        var lastSegment = uri.Segments.Length > 0
            ? Uri.UnescapeDataString(uri.Segments[^1]).Trim('/')
            : string.Empty;

        return FileNameSanitizer.Sanitize(lastSegment);
    }

    private static string GetAvailableFilePath(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate) && !File.Exists(candidate + ".part"))
        {
            return candidate;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var suffix = 1; ; suffix++)
        {
            candidate = Path.Combine(folder, $"{name} ({suffix}){extension}");
            if (!File.Exists(candidate) && !File.Exists(candidate + ".part"))
            {
                return candidate;
            }
        }
    }
}

public sealed record DownloadProgress(
    string FileName,
    long BytesDownloaded,
    long? TotalBytes,
    double BytesPerSecond,
    double? Percentage,
    int SegmentCount = 1,
    IReadOnlyList<DownloadSegmentProgress>? Segments = null,
    int ActiveConnectionCount = 1);
