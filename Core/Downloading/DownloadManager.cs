using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using HyperDL.Localization;

namespace HyperDL.Core;

public readonly record struct BatchAddResult(int Added, int Skipped, int Invalid);

/// <summary>
/// Owns the download queue, per-job lifetime, persistence, and concurrency.
/// Public members are UI-thread friendly; progress updates are marshalled to
/// the synchronization context supplied at construction.
/// </summary>
public sealed class DownloadManager : IAsyncDisposable
{
    private readonly HttpProbe _probe;
    private readonly DownloadDatabase _database;
    private readonly SynchronizationContext? _syncContext;
    private readonly ConcurrencyGate _concurrency;
    private readonly ObservableCollection<DownloadJob> _jobs = [];
    private readonly object _jobsGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _checkpointGate = new(1, 1);
    private readonly Task _checkpointTask;
    private readonly object _pendingQueueLock = new();
    private Task _pendingTail = Task.CompletedTask;
    private DownloadEngine _engine;
    private HyperDLSettings _settings;
    private bool _disposed;

    public DownloadManager(
        DownloadEngine engine,
        HttpProbe probe,
        DownloadDatabase database,
        HyperDLSettings settings,
        SynchronizationContext? syncContext = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _syncContext = syncContext;
        _concurrency = new ConcurrencyGate(
            Math.Clamp(settings.MaximumConcurrentDownloads, 1, 64));
        Jobs = new ReadOnlyObservableCollection<DownloadJob>(_jobs);
        _checkpointTask = Task.Run(CheckpointLoopAsync);
    }

    public ReadOnlyObservableCollection<DownloadJob> Jobs { get; }

    public HyperDLSettings Settings
    {
        get => _settings;
        set
        {
            _settings = value ?? throw new ArgumentNullException(nameof(value));
            _concurrency.SetLimit(Math.Clamp(value.MaximumConcurrentDownloads, 1, 64));
            _engine.SetSpeedLimit(value.SpeedLimitKBps);
        }
    }

    /// <summary>
    /// Swaps the engine used by <em>new</em> jobs. In-flight jobs keep the engine
    /// they started with, so switching folders or per-host limits is safe.
    /// </summary>
    public void UpdateEngine(DownloadEngine engine) =>
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public event EventHandler<DownloadJob>? JobCompleted;
    public event EventHandler<DownloadJob>? JobFailed;

    /// <summary>
    /// Loads persisted sessions and optionally resumes interrupted work.
    /// </summary>
    public async Task InitializeAsync(
        bool autoResume = true,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _database.LoadSessionsAsync(cancellationToken).ConfigureAwait(false);
        var persistedIds = sessions.Select(session => session.Id).ToHashSet();
        foreach (var session in sessions)
        {
            var job = new DownloadJob(
                session.Metadata,
                session,
                _settings.ConnectionCount,
                _settings.BufferMode);
            if (session.Status == DownloadSessionStatus.Completed)
            {
                job.SetTargetPath(session.FinalPath);
            }

            AddJob(job);
            if (autoResume &&
                session.Status is DownloadSessionStatus.Paused or DownloadSessionStatus.Recoverable)
            {
                StartJob(job);
            }
        }

        var pending = await _database.LoadPendingAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in pending)
        {
            if (persistedIds.Contains(entry.Id))
            {
                // A crash can happen between saving the session and deleting the
                // pending row; the session is the richer record, so drop the row.
                await _database.DeletePendingAsync(entry.Id, cancellationToken)
                    .ConfigureAwait(false);
                continue;
            }

            var job = new DownloadJob(
                CreateQueuedMetadata(entry.Url, entry.FileName),
                null,
                entry.RequestedConnections,
                entry.BufferMode,
                entry.Id);
            AddJob(job);
            if (autoResume)
            {
                StartJob(job);
            }
        }
    }

    public Task<BatchAddResult> AddTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var candidates = ParseUrls(text);
        var added = 0;
        var skipped = 0;
        var invalid = 0;

        foreach (var value in SplitCandidates(text))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                !DownloadUrlValidator.IsSupported(uri))
            {
                invalid++;
                continue;
            }

            if (AddUrl(uri))
            {
                added++;
            }
            else
            {
                skipped++;
            }
        }

        return Task.FromResult(new BatchAddResult(
            added,
            Math.Max(skipped, candidates.Count - added - invalid),
            invalid));
    }

    public bool AddUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!DownloadUrlValidator.IsSupported(uri))
        {
            throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(uri));
        }

        if (IsActiveDuplicate(uri))
        {
            return false;
        }

        var metadata = CreateQueuedMetadata(uri, null);
        var job = new DownloadJob(
            metadata,
            null,
            _settings.ConnectionCount,
            _settings.BufferMode);
        AddJob(job);
        PersistPending(job);
        StartJob(job);
        return true;
    }

    public void Pause(DownloadJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_disposed || job.Status is DownloadSessionStatus.Completed or DownloadSessionStatus.Cancelled)
        {
            return;
        }

        job.PauseRequested = true;
        var session = job.Session;
        if (session is { Status: DownloadSessionStatus.Downloading })
        {
            session.Pause();
            SetStatus(job, DownloadSessionStatus.Pausing);
            return;
        }

        if (session is not null)
        {
            session.Status = DownloadSessionStatus.Paused;
        }

        job.Cancellation?.Cancel();
        SetStatus(job, DownloadSessionStatus.Paused);
    }

    public void Resume(DownloadJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_disposed || job.Status == DownloadSessionStatus.Completed)
        {
            return;
        }

        job.PauseRequested = false;
        job.CancelRequested = false;
        if (job.Session is { Status: DownloadSessionStatus.Completed })
        {
            return;
        }

        if (job.RunTask is { IsCompleted: false })
        {
            return;
        }

        job.ClearError();
        SetStatus(job, DownloadSessionStatus.Waiting);
        StartJob(job);
    }

    public void Cancel(DownloadJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (_disposed || job.Status is DownloadSessionStatus.Completed or DownloadSessionStatus.Cancelled)
        {
            return;
        }

        job.CancelRequested = true;
        job.Cancellation?.Cancel();
        if (job.RunTask is not { IsCompleted: false })
        {
            SetStatus(job, DownloadSessionStatus.Cancelled);
        }
    }

    public void Retry(DownloadJob job) => Resume(job);

    public void PauseAll()
    {
        foreach (var job in SnapshotJobs())
        {
            if (job.Status is DownloadSessionStatus.Downloading or DownloadSessionStatus.Waiting)
            {
                Pause(job);
            }
        }
    }

    public void ResumeAll()
    {
        foreach (var job in SnapshotJobs())
        {
            if (job.Status is DownloadSessionStatus.Paused or DownloadSessionStatus.Recoverable)
            {
                Resume(job);
            }
        }
    }

    public async Task RemoveAsync(
        DownloadJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        job.CancelRequested = true;
        job.Cancellation?.Cancel();

        if (job.RunTask is { IsCompleted: false } task)
        {
            try
            {
                await task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Removal must not block on a wedged network read.
            }
        }

        lock (_jobsGate)
        {
            _jobs.Remove(job);
        }

        await _database.DeleteSessionAsync(job.Id, cancellationToken).ConfigureAwait(false);
        ForgetPending(job);
        await FlushPendingWritesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Removes jobs and deletes their completed/partial files. Returns the paths
    /// that could not be deleted so the UI can show an actionable message.
    /// </summary>
    public async Task<IReadOnlyList<string>> DeleteFilesAsync(
        IEnumerable<DownloadJob> jobs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var failures = new List<string>();
        foreach (var job in jobs.ToArray())
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (job.TargetPath is { Length: > 0 } target)
            {
                paths.Add(target);
            }

            if (job.Session?.PartPath is { Length: > 0 } part)
            {
                paths.Add(part);
            }

            await RemoveAsync(job, cancellationToken).ConfigureAwait(false);
            foreach (var path in paths)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{path}: {exception.Message}");
                }
            }
        }

        return failures;
    }

    /// <summary>
    /// Replaces the URL of a job that has not started yet. Keeps the queue row
    /// and its pending record so duplicate detection still works.
    /// </summary>
    public async Task<bool> ReplaceUrlAsync(
        DownloadJob job,
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(uri);
        if (_disposed ||
            job.Session is not null ||
            !DownloadUrlValidator.IsSupported(uri) ||
            IsActiveDuplicate(uri, job))
        {
            return false;
        }

        job.PauseRequested = false;
        job.CancelRequested = false;
        job.Cancellation?.Cancel();
        if (job.RunTask is { IsCompleted: false } running)
        {
            try
            {
                await running.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // A stuck probe must not prevent the queue row from being edited.
            }
        }

        await FlushPendingWritesAsync().ConfigureAwait(false);
        RunOnUi(() =>
        {
            job.ApplyMetadata(CreateQueuedMetadata(uri, null));
            job.ClearError();
            job.SetStatus(DownloadSessionStatus.Waiting);
        });

        await _database.SavePendingAsync(
            new PendingDownload(
                job.Id,
                job.SourceUri,
                job.FileName,
                job.RequestedConnections,
                job.BufferMode),
            cancellationToken).ConfigureAwait(false);
        StartJob(job);
        return true;
    }

    public async Task ShutdownAsync()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var job in SnapshotJobs())
        {
            var session = job.Session;
            if (session is null)
            {
                job.Cancellation?.Cancel();
                continue;
            }

            if (session.Status is DownloadSessionStatus.Downloading or DownloadSessionStatus.Pausing)
            {
                session.Pause();
                SetStatus(job, DownloadSessionStatus.Pausing);
            }
        }

        var tasks = SnapshotJobs()
            .Select(job => job.RunTask)
            .Where(task => task is { IsCompleted: false })
            .Cast<Task>()
            .ToArray();
        if (tasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false);
            }
            catch
            {
                // Closing the window should never hang on network teardown.
            }
        }

        foreach (var job in SnapshotJobs())
        {
            if (job.Session is { } session)
            {
                try
                {
                    await _database.SaveSessionAsync(session).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort checkpoint during shutdown.
                }
            }
        }

        await FlushPendingWritesAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await FlushPendingWritesAsync().ConfigureAwait(false);
        _lifetime.Cancel();
        try
        {
            await _checkpointTask.ConfigureAwait(false);
        }
        catch
        {
            // Ignore cancellation of the checkpoint loop.
        }

        foreach (var job in SnapshotJobs())
        {
            job.Cancellation?.Cancel();
        }

        _lifetime.Dispose();
        _checkpointGate.Dispose();
    }

    private static DownloadMetadata CreateQueuedMetadata(Uri uri, string? fileName) =>
        new(
            uri,
            null,
            FileNameSanitizer.Sanitize(
                string.IsNullOrWhiteSpace(fileName) ? GetFileName(uri) : fileName),
            null,
            null,
            null,
            null,
            false,
            ByteRangeSupport.Unknown)
        {
            OriginalUri = uri
        };

    private void PersistPending(DownloadJob job) =>
        QueuePendingOperation(token => _database.SavePendingAsync(
            new PendingDownload(
                job.Id,
                job.SourceUri,
                job.FileName,
                job.RequestedConnections,
                job.BufferMode),
            token));

    private void ForgetPending(DownloadJob job) =>
        QueuePendingOperation(token => _database.DeletePendingAsync(job.Id, token));

    private void QueuePendingOperation(Func<CancellationToken, Task> operation)
    {
        lock (_pendingQueueLock)
        {
            var previous = _pendingTail;
            _pendingTail = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                }
                catch
                {
                    // A previous best-effort write must not block the queue.
                }

                try
                {
                    await operation(_lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Shutdown won the race; persistence is best effort.
                }
                catch
                {
                    // Database failures are surfaced by the checkpoint loop.
                }
            });
        }
    }

    /// <summary>
    /// Waits until all fire-and-forget queue writes have been applied. Tests and
    /// graceful shutdown use this to make persistence deterministic.
    /// </summary>
    public async Task FlushPendingWritesAsync()
    {
        Task tail;
        lock (_pendingQueueLock)
        {
            tail = _pendingTail;
        }

        try
        {
            await tail.ConfigureAwait(false);
        }
        catch
        {
            // Best effort.
        }
    }

    private void AddJob(DownloadJob job)
    {
        RunOnUi(() =>
        {
            lock (_jobsGate)
            {
                _jobs.Insert(0, job);
            }
        });
    }

    private void StartJob(DownloadJob job)
    {
        job.Cancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        job.Cancellation = cancellation;
        job.RunTask = ExecuteJobAsync(job, cancellation.Token);
    }

    private async Task ExecuteJobAsync(DownloadJob job, CancellationToken cancellationToken)
    {
        ConcurrencyGate.Lease? lease = null;
        try
        {
            SetStatus(job, DownloadSessionStatus.Waiting);
            lease = await _concurrency.AcquireAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            SetStatus(job, DownloadSessionStatus.Downloading);
            var sourceUri = job.Metadata.OriginalUri ?? job.Metadata.FinalUri;
            var metadata = await _probe.ProbeAsync(sourceUri, cancellationToken)
                .ConfigureAwait(false);

            var session = job.Session;
            if (session is null)
            {
                RunOnUi(() => job.ApplyMetadata(metadata));
                if (metadata.RangeSupport == ByteRangeSupport.Supported &&
                    metadata.ContentLength is > 0)
                {
                    session = _engine.CreateSession(
                        metadata,
                        job.RequestedConnections,
                        job.Id);
                    session.BufferModeOverride = job.BufferMode;
                    session.DynamicSegmentationOverride = _settings.DynamicSegmentation;
                    RunOnUi(() => job.AttachSession(session));
                    await _database.SaveSessionAsync(session, cancellationToken)
                        .ConfigureAwait(false);
                    ForgetPending(job);
                }
            }
            else
            {
                // Never stitch a resumed partial file to a different remote file.
                session.EnsureSourceUnchanged(metadata);
                session.BufferModeOverride = job.BufferMode;
                session.DynamicSegmentationOverride = _settings.DynamicSegmentation;
            }

            var progress = new CallbackProgress<DownloadProgress>(update =>
            {
                RunOnUi(() => job.UpdateProgress(update));
            });

            if (session is not null)
            {
                var path = await _engine.DownloadAsync(
                    session,
                    progress,
                    cancellationToken,
                    job.BufferMode).ConfigureAwait(false);
                await _database.SaveSessionAsync(session).ConfigureAwait(false);
                RunOnUi(() =>
                {
                    job.SetTargetPath(path);
                    job.SetStatus(DownloadSessionStatus.Completed);
                    JobCompleted?.Invoke(this, job);
                });
            }
            else
            {
                var path = await _engine.DownloadAsync(
                    metadata,
                    progress,
                    cancellationToken,
                    job.RequestedConnections,
                    job.BufferMode).ConfigureAwait(false);
                RunOnUi(() =>
                {
                    job.SetTargetPath(path);
                    job.SetStatus(DownloadSessionStatus.Completed);
                    JobCompleted?.Invoke(this, job);
                });
            }
        }
        catch (OperationCanceledException)
        {
            var session = job.Session;
            var paused = job.PauseRequested ||
                         session?.Status is DownloadSessionStatus.Paused or DownloadSessionStatus.Pausing;
            if (paused)
            {
                if (session is not null && session.Status != DownloadSessionStatus.Paused)
                {
                    session.Status = DownloadSessionStatus.Paused;
                }

                SetStatus(job, DownloadSessionStatus.Paused);
                await SaveQuietlyAsync(session).ConfigureAwait(false);
                if (session is null)
                {
                    ForgetPending(job);
                }
            }
            else
            {
                if (session is not null && session.Status != DownloadSessionStatus.Cancelled)
                {
                    session.Status = DownloadSessionStatus.Cancelled;
                }

                SetStatus(job, DownloadSessionStatus.Cancelled);
                await SaveQuietlyAsync(session).ConfigureAwait(false);
                ForgetPending(job);
            }
        }
        catch (Exception exception)
        {
            var session = job.Session;
            var status = session?.Status is DownloadSessionStatus.Recoverable
                ? DownloadSessionStatus.Recoverable
                : DownloadSessionStatus.Failed;
            if (session is not null)
            {
                session.Status = status;
                await SaveQuietlyAsync(session).ConfigureAwait(false);
            }
            else
            {
                ForgetPending(job);
            }

            RunOnUi(() =>
            {
                job.SetError(exception, status);
                JobFailed?.Invoke(this, job);
            });
        }
        finally
        {
            lease?.Dispose();
        }
    }

    private async Task SaveQuietlyAsync(DownloadSession? session)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await _database.SaveSessionAsync(session).ConfigureAwait(false);
        }
        catch
        {
            // The next checkpoint or shutdown will try again.
        }
    }

    private async Task CheckpointLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (!await _checkpointGate.WaitAsync(0, _lifetime.Token).ConfigureAwait(false))
                {
                    continue;
                }

                try
                {
                    foreach (var job in SnapshotJobs())
                    {
                        var session = job.Session;
                        if (session is null ||
                            session.Status is not (DownloadSessionStatus.Downloading or
                                                   DownloadSessionStatus.Pausing or
                                                   DownloadSessionStatus.Paused))
                        {
                            continue;
                        }

                        await _database.SaveSessionAsync(session, _lifetime.Token)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch
                {
                    // A locked or busy database should not kill the checkpoint loop.
                }
                finally
                {
                    _checkpointGate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private bool IsActiveDuplicate(Uri uri, DownloadJob? except = null)
    {
        foreach (var job in SnapshotJobs())
        {
            if (ReferenceEquals(job, except) ||
                job.Status is DownloadSessionStatus.Completed or
                DownloadSessionStatus.Cancelled or
                DownloadSessionStatus.Failed)
            {
                continue;
            }

            if (UriEquals(job.SourceUri, uri) ||
                (job.Session is { } session && UriEquals(session.Metadata.FinalUri, uri)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UriEquals(Uri left, Uri right) =>
        string.Equals(
            left.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped),
            right.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped),
            StringComparison.OrdinalIgnoreCase);

    private DownloadJob[] SnapshotJobs()
    {
        lock (_jobsGate)
        {
            return [.. _jobs];
        }
    }

    private void SetStatus(DownloadJob job, DownloadSessionStatus status) =>
        RunOnUi(() => job.SetStatus(status));

    private void RunOnUi(Action action)
    {
        if (_syncContext is null || SynchronizationContext.Current == _syncContext)
        {
            action();
            return;
        }

        _syncContext.Post(_ => action(), null);
    }

    private static IReadOnlyList<Uri> ParseUrls(string text) =>
        SplitCandidates(text)
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                             DownloadUrlValidator.IsSupported(uri)
                ? uri
                : null)
            .Where(uri => uri is not null)
            .Cast<Uri>()
            .DistinctBy(uri => uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped))
            .ToArray();

    private static IEnumerable<string> SplitCandidates(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var raw in text.Split(
                     ['\r', '\n', '\t', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var value = raw.Trim().Trim('"', '\'', '<', '>', ',', ';');
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static string GetFileName(Uri uri)
    {
        var lastSegment = uri.Segments.Length > 0
            ? Uri.UnescapeDataString(uri.Segments[^1]).Trim('/')
            : string.Empty;
        return string.IsNullOrWhiteSpace(lastSegment) ? "download" : lastSegment;
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
