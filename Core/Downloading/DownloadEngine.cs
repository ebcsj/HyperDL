using System.Buffers;
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
    public const long MinimumSegmentSize = 4L * 1024 * 1024;
    public const int LowRamBufferSize = 64 * 1024;
    public const int BalancedBufferSize = 256 * 1024;
    public const int FastBufferSize = 1024 * 1024;
    public const int AutoDefaultBufferSize = 256 * 1024;
    public const int AutoLargeBufferSize = 512 * 1024;
    public const int AutoMinimumBufferSize = 128 * 1024;
    public const long GlobalNetworkBufferBudgetBytes = 64L * 1024 * 1024;

    private readonly HttpClient _client;
    private readonly string _downloadDirectory;
    private readonly DownloadEngineOptions _options;
    private readonly GlobalConnectionLimiter _connectionLimiter;
    private readonly RetryPolicy _retryPolicy;
    private readonly BandwidthLimiter _bandwidthLimiter;

    /// <summary>
    /// Informational per-connection buffer size for the configured mode.
    /// Actual downloads size their buffers from the number of possible connections.
    /// </summary>
    public int BufferSize => _options.BufferMode.ResolveBufferSize(1);

    public BufferMode BufferMode => _options.BufferMode;

    public int SpeedLimitKBps => _bandwidthLimiter.LimitKBps;

    public void SetSpeedLimit(int kilobytesPerSecond) =>
        _bandwidthLimiter.SetLimit(kilobytesPerSecond);

    public DownloadEngine(
        HttpClient? client = null,
        string? downloadDirectory = null,
        bool lowRamMode = false,
        GlobalConnectionLimiter? connectionLimiter = null)
        : this(
            client,
            downloadDirectory,
            new DownloadEngineOptions
            {
                BufferMode = lowRamMode ? BufferMode.LowRam : BufferMode.Balanced,
                DynamicSegmentation = false
            },
            connectionLimiter)
    {
    }

    public DownloadEngine(
        HttpClient? client,
        string? downloadDirectory,
        DownloadEngineOptions options,
        GlobalConnectionLimiter? connectionLimiter = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Validate();
        _client = client ?? CreateHttpClient(_options);
        _downloadDirectory = downloadDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        _connectionLimiter = connectionLimiter ?? GlobalConnectionLimiter.Shared;
        _retryPolicy = new RetryPolicy(
            _options.MaximumRetryAttempts,
            _options.RetryBaseDelay,
            _options.RetryMaximumDelay);
        _bandwidthLimiter = new BandwidthLimiter(_options.SpeedLimitKBps);
    }

    public static int GetSegmentCount(DownloadMetadata metadata, int requestedConnectionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (requestedConnectionCount is not (0 or 1 or 2 or 4 or 8 or 16 or 32 or 64))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedConnectionCount),
                "Connections must be Auto, 1, 2, 4, 8, 16, 32, or 64.");
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
        int requestedConnectionCount = 0,
        BufferMode? bufferMode = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (!DownloadUrlValidator.IsSupported(metadata.FinalUri))
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL.", nameof(metadata));
        }

        var fileName = FileNameSanitizer.Sanitize(metadata.FileName);
        if (metadata.RangeSupport == ByteRangeSupport.Supported &&
            metadata.ContentLength is > 0)
        {
            var session = CreateSession(metadata, requestedConnectionCount);
            session.BufferModeOverride = bufferMode;
            return DownloadAsync(session, progress, cancellationToken);
        }

        return DownloadSingleAsync(metadata.FinalUri, progress, cancellationToken, fileName, bufferMode);
    }

    public DownloadSession CreateSession(
        DownloadMetadata metadata,
        int requestedConnectionCount = 0,
        Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (!DownloadUrlValidator.IsSupported(metadata.FinalUri) ||
            metadata.RangeSupport != ByteRangeSupport.Supported ||
            metadata.ContentLength is not > 0)
        {
            throw new ArgumentException(
                "A range-capable HTTP/HTTPS file with a known size is required for a resumable download.",
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
        return new DownloadSession(metadata, fileName, finalPath, connectionCount, segments, id);
    }

    public Task<string> DownloadAsync(
        DownloadSession session,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        BufferMode? bufferMode = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (bufferMode is { } selected)
        {
            session.BufferModeOverride = selected;
        }

        session.PrepareResume();
        session.Status = DownloadSessionStatus.Downloading;
        return DownloadSegmentedAsync(session, progress, cancellationToken);
    }

    public Task<string> DownloadAsync(
        Uri uri,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        string? suggestedFileName = null,
        BufferMode? bufferMode = null)
        => DownloadSingleAsync(uri, progress, cancellationToken, suggestedFileName, bufferMode);

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
        string? suggestedFileName,
        BufferMode? bufferMode)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!DownloadUrlValidator.IsSupported(uri))
        {
            throw new ArgumentException("Enter a valid HTTP or HTTPS URL.", nameof(uri));
        }

        Directory.CreateDirectory(_downloadDirectory);

        var fileName = FileNameSanitizer.Sanitize(
            string.IsNullOrWhiteSpace(suggestedFileName) ? GetFileName(uri) : suggestedFileName);
        var outputPath = GetAvailableFilePath(_downloadDirectory, fileName);
        var partPath = outputPath + ".part";
        var mode = bufferMode ?? _options.BufferMode;
        var bufferSize = mode.ResolveBufferSize(1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await DownloadSingleOnceAsync(
                    uri,
                    fileName,
                    partPath,
                    outputPath,
                    bufferSize,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                _retryPolicy.IsTransientFailure(exception) &&
                attempt < _retryPolicy.MaximumAttempts &&
                !cancellationToken.IsCancellationRequested)
            {
                var transient = exception as TransientDownloadException;
                var delay = _retryPolicy.GetDelay(attempt, transient);
                progress?.Report(new DownloadProgress(
                    fileName,
                    File.Exists(partPath) ? new FileInfo(partPath).Length : 0,
                    null,
                    0,
                    0));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<string> DownloadSingleOnceAsync(
        Uri uri,
        string fileName,
        string partPath,
        string outputPath,
        int bufferSize,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(uri, range: null, metadata: null);
        using var connectionLease = await _connectionLimiter.AcquireAsync(cancellationToken)
            .ConfigureAwait(false);
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        await using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        var totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is > 0)
        {
            DiskSpaceGuard.EnsureAvailable(_downloadDirectory, totalBytes.Value);
        }

        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long bytesDownloaded = 0;
        var stopwatch = Stopwatch.StartNew();
        var lastReportTime = stopwatch.Elapsed;
        long lastReportedBytes = 0;
        double smoothedSpeed = 0;

        try
        {
            await using var fileStream = new FileStream(
                partPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: bufferSize,
                options: FileOptions.Asynchronous | FileOptions.SequentialScan);

            ReportProgress(force: true);

            while (true)
            {
                var bytesRead = await ReadWithIdleTimeoutAsync(
                    networkStream,
                    buffer.AsMemory(0, bufferSize),
                    idleTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                await _bandwidthLimiter.ThrottleAsync(bytesRead, cancellationToken)
                    .ConfigureAwait(false);
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                    .ConfigureAwait(false);
                bytesDownloaded += bytesRead;

                if (stopwatch.Elapsed - lastReportTime >= TimeSpan.FromMilliseconds(250))
                {
                    ReportProgress();
                }
            }

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            fileStream.Flush(flushToDisk: true);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (totalBytes.HasValue && bytesDownloaded != totalBytes.Value)
        {
            throw new InvalidDataException(
                $"Download length mismatch: expected {totalBytes.Value} bytes, got {bytesDownloaded}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        File.Move(partPath, outputPath, overwrite: false);
        ReportProgress(force: true, isComplete: true);
        return outputPath;

        void ReportProgress(bool force = false, bool isComplete = false)
        {
            var elapsed = stopwatch.Elapsed - lastReportTime;
            if (!force && elapsed < TimeSpan.FromMilliseconds(250))
            {
                return;
            }

            var rawSpeed = elapsed.TotalSeconds > 0
                ? (bytesDownloaded - lastReportedBytes) / elapsed.TotalSeconds
                : 0;
            smoothedSpeed = smoothedSpeed <= 0 ? rawSpeed : smoothedSpeed * 0.7 + rawSpeed * 0.3;
            var percentage = totalBytes.HasValue
                ? totalBytes.Value == 0
                    ? isComplete ? 100 : 0
                    : Math.Clamp(bytesDownloaded * 100d / totalBytes.Value, 0, 100)
                : (double?)null;

            try
            {
                progress?.Report(new DownloadProgress(
                    fileName,
                    bytesDownloaded,
                    totalBytes,
                    smoothedSpeed,
                    percentage));
            }
            catch
            {
                // Never let a UI callback break the transfer.
            }

            lastReportTime = stopwatch.Elapsed;
            lastReportedBytes = bytesDownloaded;
        }
    }

    private async Task<string> DownloadSegmentedAsync(
        DownloadSession session,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var totalBytes = session.Metadata.ContentLength!.Value;
        long downloadedBytes;
        lock (session.SyncRoot)
        {
            downloadedBytes = session.Segments.Sum(segment => segment.BytesDownloaded);
        }

        var downloadDirectory = Path.GetDirectoryName(session.PartPath) ?? _downloadDirectory;
        DiskSpaceGuard.EnsureAvailable(
            downloadDirectory,
            Math.Max(0, totalBytes - downloadedBytes));
        var mode = session.BufferModeOverride ?? _options.BufferMode;
        var dynamicSegmentation = _options.DynamicSegmentation &&
                                  (session.DynamicSegmentationOverride ?? true) &&
                                  totalBytes >= _options.DynamicSegmentationThresholdBytes &&
                                  session.ConnectionCount >= 2;
        var maximumSegments = dynamicSegmentation
            ? _options.MaximumDynamicSegmentCount
            : session.ConnectionCount;
        var bufferSize = mode.ResolveBufferSize(
            dynamicSegmentation
                ? Math.Min(_options.MaximumDynamicSegmentCount, DownloadEngine.MaximumSegmentCount * 2)
                : session.ConnectionCount);
        session.ResolvedBufferSize = bufferSize;
        var scheduler = new SegmentScheduler(
            session.Segments,
            session.SyncRoot,
            dynamicSegmentation,
            maximumSegments,
            _options.MinimumDynamicSegmentSize);
        session.Scheduler = scheduler;
        var reporter = new DownloadProgressReporter(
            scheduler,
            progress,
            session.FileName,
            totalBytes,
            session.SyncRoot);
        var partPath = session.PartPath;
        var partCreated = File.Exists(partPath);
        FileStream? partStream = null;

        try
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                session.PauseToken);
            var token = linkedCancellation.Token;

            partStream = new FileStream(
                partPath,
                partCreated ? FileMode.Open : FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                bufferSize,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            partCreated = true;
            if (partStream.Length == 0)
            {
                partStream.SetLength(totalBytes);
            }
            else if (partStream.Length != totalBytes)
            {
                throw new InvalidDataException(
                    "The partial file length does not match the remote file size.");
            }

            var safeFileHandle = partStream.SafeFileHandle;
            Exception? firstFailure = null;
            var initialWorkerCount = Math.Max(1, Math.Min(session.ConnectionCount, scheduler.SegmentCount));
            var workers = Enumerable.Range(0, initialWorkerCount)
                .Select(_ => RunWorkerAsync())
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
            if (session.PauseRequested)
            {
                throw new OperationCanceledException(session.PauseToken);
            }

            if (!scheduler.Validate(totalBytes, out var validationError) ||
                reporter.TotalDownloaded != totalBytes ||
                partStream.Length != totalBytes)
            {
                throw new InvalidDataException(
                    validationError ?? "The final segment map or downloaded length is invalid.");
            }

            await partStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            partStream.Flush(flushToDisk: true);
            await partStream.DisposeAsync().ConfigureAwait(false);
            partStream = null;
            File.Move(partPath, session.FinalPath, overwrite: false);
            session.Status = DownloadSessionStatus.Completed;
            reporter.Report(force: true);
            return session.FinalPath;

            async Task RunWorkerAsync()
            {
                while (!token.IsCancellationRequested)
                {
                    DownloadSegment? segment;
                    if (!scheduler.TryTake(out segment))
                    {
                        if (!scheduler.TrySplit(out segment) || segment is null)
                        {
                            return;
                        }
                    }

                    try
                    {
                        await DownloadSegmentWithRetriesAsync(
                            session,
                            segment!,
                            totalBytes,
                            safeFileHandle,
                            reporter,
                            linkedCancellation,
                            token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        Interlocked.CompareExchange(ref firstFailure, exception, null);
                        linkedCancellation.Cancel();
                        throw;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (session.PauseRequested)
        {
            TryFlush(partStream);
            session.Status = DownloadSessionStatus.Paused;
            reporter.Report(force: true);
            throw;
        }
        catch (OperationCanceledException)
        {
            TryFlush(partStream);
            session.Status = DownloadSessionStatus.Cancelled;
            reporter.Report(force: true);
            throw;
        }
        catch
        {
            TryFlush(partStream);
            session.Status = DownloadSessionStatus.Recoverable;
            reporter.Report(force: true);
            throw;
        }
        finally
        {
            if (partStream is not null)
            {
                await partStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task DownloadSegmentWithRetriesAsync(
        DownloadSession session,
        DownloadSegment segment,
        long totalBytes,
        SafeFileHandle safeFileHandle,
        DownloadProgressReporter reporter,
        CancellationTokenSource linkedCancellation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            try
            {
                await DownloadSegmentOnceAsync(
                    session,
                    segment,
                    totalBytes,
                    safeFileHandle,
                    reporter,
                    cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                reporter.SetStatus(segment, DownloadSegmentStatus.Cancelled);
                throw;
            }
            catch (Exception exception) when (
                _retryPolicy.IsTransientFailure(exception) &&
                attempt < _retryPolicy.MaximumAttempts &&
                !cancellationToken.IsCancellationRequested)
            {
                reporter.SetStatus(segment, DownloadSegmentStatus.Pending, exception.Message);
                var delay = _retryPolicy.GetDelay(
                    attempt,
                    exception as TransientDownloadException);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                reporter.SetStatus(segment, DownloadSegmentStatus.Failed, exception.Message);
                throw;
            }
        }
    }

    private async Task DownloadSegmentOnceAsync(
        DownloadSession session,
        DownloadSegment segment,
        long totalBytes,
        SafeFileHandle safeFileHandle,
        DownloadProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        if (segment.CurrentOffset > segment.EndOffset)
        {
            session.Scheduler?.MarkCompleted(segment);
            return;
        }

        reporter.SetStatus(segment, DownloadSegmentStatus.Downloading);
        using var connectionLease = await _connectionLimiter.AcquireAsync(cancellationToken)
            .ConfigureAwait(false);

        var requestStart = segment.CurrentOffset;
        var requestEnd = segment.EndOffset;
        using var request = CreateRequest(
            session.Metadata.FinalUri,
            new ByteRange(requestStart, requestEnd),
            session.Metadata);
        using var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.TooManyRequests or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or
            HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout)
        {
            throw new TransientDownloadException(
                $"HTTP {(int)response.StatusCode} while requesting range {requestStart}-{requestEnd}.",
                response.StatusCode,
                GetRetryAfter(response));
        }

        if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                throw new HttpRequestException(
                    $"HTTP {(int)response.StatusCode} while requesting the file.",
                    null,
                    response.StatusCode);
            }

            throw new InvalidDataException(
                $"HTTP Range {requestStart}-{requestEnd} returned status {(int)response.StatusCode}.");
        }

        ValidateSegmentResponse(response, requestStart, requestEnd, totalBytes);

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var idleTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var buffer = ArrayPool<byte>.Shared.Rent(session.ResolvedBufferSize);

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int readLength;
                lock (session.SyncRoot)
                {
                    var remainingAtReadStart = segment.EndOffset - segment.CurrentOffset + 1;
                    if (remainingAtReadStart <= 0)
                    {
                        break;
                    }

                    readLength = (int)Math.Min(buffer.Length, remainingAtReadStart);
                }

                var bytesRead = await ReadWithIdleTimeoutAsync(
                    responseStream,
                    buffer.AsMemory(0, readLength),
                    idleTimeout,
                    cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                if (bytesRead > readLength)
                {
                    throw new InvalidDataException(
                        $"Segment {segment.Index} returned more data than requested.");
                }

                await _bandwidthLimiter.ThrottleAsync(bytesRead, cancellationToken)
                    .ConfigureAwait(false);

                long fileOffset;
                int writable;
                lock (session.SyncRoot)
                {
                    fileOffset = segment.CurrentOffset;
                    writable = (int)Math.Clamp(
                        segment.EndOffset - fileOffset + 1,
                        0,
                        bytesRead);
                    segment.IsWriting = writable > 0;
                }

                if (writable > 0)
                {
                    try
                    {
                        await RandomAccess.WriteAsync(
                            safeFileHandle,
                            buffer.AsMemory(0, writable),
                            fileOffset,
                            cancellationToken).ConfigureAwait(false);
                        reporter.RecordWritten(segment, writable);
                    }
                    finally
                    {
                        lock (session.SyncRoot)
                        {
                            segment.IsWriting = false;
                        }
                    }
                }

                reporter.Report();
                if (segment.CurrentOffset > segment.EndOffset)
                {
                    break;
                }
            }

            if (segment.CurrentOffset <= segment.EndOffset)
            {
                throw new InvalidDataException(
                    $"Segment {segment.Index} ended early: expected {segment.ExpectedLength} bytes, " +
                    $"received {segment.BytesDownloaded} bytes.");
            }

            // Detect a server that sends more bytes than the requested range.
            // Skip the check when this range was split, because the extra bytes
            // legitimately belong to the newly created range.
            if (segment.EndOffset == requestEnd)
            {
                var probe = ArrayPool<byte>.Shared.Rent(1);
                try
                {
                    var extraBytes = await ReadWithIdleTimeoutAsync(
                        responseStream,
                        probe.AsMemory(0, 1),
                        idleTimeout,
                        cancellationToken).ConfigureAwait(false);
                    if (extraBytes > 0)
                    {
                        throw new InvalidDataException(
                            $"Segment {segment.Index} returned more data than requested.");
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(probe);
                }
            }

            session.Scheduler?.MarkCompleted(segment);
            reporter.SetStatus(segment, DownloadSegmentStatus.Completed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
        if (contentRange is null ||
            !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            contentRange.From != expectedStart ||
            contentRange.To != expectedEnd ||
            contentRange.To < contentRange.From ||
            contentRange.Length != totalBytes ||
            response.Content.Headers.ContentLength is long contentLength &&
            contentLength != expectedLength)
        {
            throw new InvalidDataException(
                $"HTTP Range {expectedStart}-{expectedEnd} returned an invalid Content-Range.");
        }
    }

    private static HttpRequestMessage CreateRequest(
        Uri uri,
        ByteRange? range,
        DownloadMetadata? metadata)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        if (range is { } selectedRange)
        {
            request.Headers.Range = new RangeHeaderValue(
                selectedRange.StartOffset,
                selectedRange.EndOffset);
            AddIfRange(request, metadata);
        }

        return request;
    }

    private static void AddIfRange(HttpRequestMessage request, DownloadMetadata? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(metadata.ETag))
        {
            try
            {
                request.Headers.IfRange = new RangeConditionHeaderValue(
                    new EntityTagHeaderValue(metadata.ETag));
                return;
            }
            catch (FormatException)
            {
                // Fall back to Last-Modified when an ETag is malformed.
            }
        }

        if (metadata.LastModified is { } lastModified)
        {
            request.Headers.IfRange = new RangeConditionHeaderValue(lastModified);
        }
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        return null;
    }

    private static Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.IsSuccessStatusCode)
        {
            return Task.CompletedTask;
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests or
            HttpStatusCode.RequestTimeout or
            >= HttpStatusCode.InternalServerError)
        {
            throw new TransientDownloadException(
                $"HTTP {(int)response.StatusCode}.",
                response.StatusCode,
                GetRetryAfter(response));
        }

        throw new HttpRequestException(
            $"HTTP {(int)response.StatusCode}.",
            null,
            response.StatusCode);
    }

    private async ValueTask<int> ReadWithIdleTimeoutAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationTokenSource idleTimeout,
        CancellationToken cancellationToken)
    {
        try
        {
            // Re-arm the timeout for every read. The linked source is owned by
            // the response scope, so timing out cancels the underlying read
            // before the caller can return the pooled buffer.
            idleTimeout.CancelAfter(_options.IdleTimeout);
            return await stream.ReadAsync(buffer, idleTimeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TransientDownloadException(
                "The connection stalled while waiting for data.");
        }
    }

    private static void TryFlush(FileStream? stream)
    {
        if (stream is null)
        {
            return;
        }

        try
        {
            stream.Flush(flushToDisk: true);
        }
        catch
        {
            // Best effort; the offsets saved to the database are still correct.
        }
    }

    private static HttpClient CreateHttpClient(DownloadEngineOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            MaxConnectionsPerServer = options.MaximumConnectionsPerHost > 0
                ? options.MaximumConnectionsPerHost
                : MaximumSegmentCount
        };

        return new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
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
