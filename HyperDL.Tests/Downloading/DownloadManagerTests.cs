using System.IO;
using System.Net;
using System.Net.Http.Headers;
using HyperDL.Core;
using Microsoft.Data.Sqlite;

namespace HyperDL.Tests;

public class DownloadManagerTests : IDisposable
{
    private static readonly byte[] FileBytes =
        CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));

    private readonly string _directory;
    private readonly DownloadDatabase _database;

    public DownloadManagerTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            $"HyperDL.Manager-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _database = new DownloadDatabase(Path.Combine(_directory, "manager.db"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task AddUrl_DownloadsPersistsAndCompletes()
    {
        await using var manager = CreateManager(out var handler);
        handler.Files["/a.bin"] = FileBytes;

        Assert.True(manager.AddUrl(new Uri("https://unit.test/a.bin")));
        var job = manager.Jobs.Single();
        await job.RunTask!;

        Assert.Equal(DownloadSessionStatus.Completed, job.Status);
        var target = Path.Combine(_directory, "a.bin");
        Assert.Equal(target, job.TargetPath);
        Assert.True(File.Exists(target));
        Assert.Equal(FileBytes, await File.ReadAllBytesAsync(target));
        Assert.Empty(await _database.LoadPendingAsync());
    }

    [Fact]
    public async Task AddUrl_RejectsActiveDuplicate()
    {
        await using var manager = CreateManager(out var handler);
        handler.Files["/a.bin"] = FileBytes;
        handler.OpenGate();

        Assert.True(manager.AddUrl(new Uri("https://unit.test/a.bin")));
        var first = manager.Jobs.Single();
        Assert.True(
            first.Status is DownloadSessionStatus.Waiting or DownloadSessionStatus.Downloading,
            $"status={first.Status}");
        Assert.False(manager.AddUrl(new Uri("https://unit.test/a.bin")));
        Assert.Single(manager.Jobs);

        handler.ReleaseGate();
        var job = manager.Jobs.Single();
        await job.RunTask!;
        Assert.Equal(DownloadSessionStatus.Completed, job.Status);
    }

    [Fact]
    public async Task AddText_BatchAddsDistinctUrlsAndSkipsDuplicates()
    {
        await using var manager = CreateManager(out var handler);
        handler.Files["/a.bin"] = FileBytes;
        handler.Files["/b.bin"] = FileBytes;
        handler.OpenGate();

        var result = await manager.AddTextAsync(
            "https://unit.test/a.bin\nhttps://unit.test/b.bin https://unit.test/a.bin");

        Assert.Equal(2, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Invalid);

        handler.ReleaseGate();
        await Task.WhenAll(manager.Jobs.Select(job => job.RunTask!));
    }

    [Fact]
    public async Task Remove_RemovesJobAndPersistedRecords()
    {
        await using var manager = CreateManager(out var handler);
        handler.Files["/a.bin"] = FileBytes;

        Assert.True(manager.AddUrl(new Uri("https://unit.test/a.bin")));
        var job = manager.Jobs.Single();
        await job.RunTask!;
        Assert.Equal(DownloadSessionStatus.Completed, job.Status);

        await manager.RemoveAsync(job);

        Assert.Empty(manager.Jobs);
        Assert.Empty(await _database.LoadSessionsAsync());
        Assert.Empty(await _database.LoadPendingAsync());
    }

    [Fact]
    public async Task Pause_KeepsPartAndOffsetsThenResumeCompletes()
    {
        await using var manager = CreateManager(out var handler);
        handler.Files["/a.bin"] = FileBytes;

        Assert.True(manager.AddUrl(new Uri("https://unit.test/a.bin")));
        var job = manager.Jobs.Single();
        await WaitUntilAsync(
            () => job.Status == DownloadSessionStatus.Downloading && job.BytesDownloaded > 0);

        manager.Pause(job);
        await WaitUntilAsync(() => job.Status == DownloadSessionStatus.Paused);

        var session = job.Session!;
        Assert.True(File.Exists(session.PartPath));
        var pausedBytes = session.Segments.Sum(segment => segment.BytesDownloaded);
        Assert.InRange(pausedBytes, 1, FileBytes.LongLength - 1);

        manager.Resume(job);
        await WaitUntilAsync(() => job.Status == DownloadSessionStatus.Completed);

        Assert.Equal(FileBytes, await File.ReadAllBytesAsync(job.TargetPath!));
        Assert.False(File.Exists(session.PartPath));
    }

    [Fact]
    public async Task MaximumConcurrentDownloads_SerializesActiveJobs()
    {
        await using var manager = CreateManager(out var handler, maximumConcurrentDownloads: 1);
        // Small single-segment files keep exactly one body stream open per job.
        var singleSegment = CreatePatternedBytes(1024 * 1024);
        handler.Files["/a.bin"] = singleSegment;
        handler.Files["/b.bin"] = singleSegment;

        Assert.True(manager.AddUrl(new Uri("https://unit.test/a.bin")));
        Assert.True(manager.AddUrl(new Uri("https://unit.test/b.bin")));

        await Task.WhenAll(manager.Jobs.Select(job => job.RunTask!));

        Assert.All(manager.Jobs, job => Assert.Equal(DownloadSessionStatus.Completed, job.Status));
        Assert.Equal(1, handler.MaxActiveStreams);
    }

    private DownloadManager CreateManager(
        out RangeFileHandler handler,
        int maximumConcurrentDownloads = 2)
    {
        handler = new RangeFileHandler();
        var settings = new HyperDLSettings
        {
            DownloadDirectory = _directory,
            ConnectionCount = 2,
            MaximumConcurrentDownloads = maximumConcurrentDownloads,
            BufferMode = BufferMode.Auto,
            AutoResume = false
        };
        var engine = new DownloadEngine(
            new HttpClient(handler),
            _directory,
            new DownloadEngineOptions());
        return new DownloadManager(
            engine,
            new HttpProbe(new HttpClient(handler)),
            _database,
            settings,
            syncContext: null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutSeconds = 30)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(timeoutSeconds))
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(50);
        }
    }

    private static byte[] CreatePatternedBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index * 31);
        }

        return bytes;
    }

    /// <summary>
    /// Serves the configured files over loopback-shaped HTTP: a bytes=0-0 probe
    /// gets one byte, engine range requests get the matching slice, and every
    /// body stream reports its active reads so tests can observe concurrency.
    /// </summary>
    private sealed class RangeFileHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _sync = new();
        private int _activeStreams;
        private int _engineRequests;
        private TaskCompletionSource? _gate;
        private TaskCompletionSource? _pauseRelease;

        public int MaxActiveStreams { get; private set; }

        public Dictionary<string, byte[]> Files => _files;

        public IDisposable OpenGate()
        {
            _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return new GateScope(this);
        }

        public void ReleasePause() => _pauseRelease?.TrySetResult();

        public void ReleaseGate() => _gate?.TrySetResult();

        internal void EnterStream()
        {
            var current = Interlocked.Increment(ref _activeStreams);
            lock (_sync)
            {
                if (current > MaxActiveStreams)
                {
                    MaxActiveStreams = current;
                }
            }
        }

        internal void ExitStream() => Interlocked.Decrement(ref _activeStreams);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            // Await the gate asynchronously: a blocking Wait here would stall
            // the caller's thread and break the surrounding async flow.
            if (_gate is { } gate)
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            var path = request.RequestUri!.AbsolutePath;
            var bytes = _files[path];
            var range = request.Headers.Range?.Ranges.SingleOrDefault();
            var from = range?.From ?? 0;
            var to = range?.To ?? bytes.LongLength - 1;
            var sliceLength = (int)(to - from + 1);
            var slice = new byte[sliceLength];
            Array.Copy(bytes, (int)from, slice, 0, sliceLength);

            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(new CountingStream(slice, this))
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                from,
                to,
                bytes.LongLength);
            response.Content.Headers.ContentLength = sliceLength;

            if (from == 0 && to == 0)
            {
                // bytes=0-0 is the probe shape; everything else is an engine read.
                return response;
            }

            if (Interlocked.Increment(ref _engineRequests) == 1)
            {
                // Gate the first engine read so a test can pause mid-transfer.
                // The replacement content needs its own range headers: headers
                // set on the previous content object do not carry over.
                _pauseRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                response.Content.Dispose();
                var paused = new StreamContent(
                    new PausingStream(slice, _pauseRelease, () => EnterStream(), () => ExitStream()));
                paused.Headers.ContentRange = new ContentRangeHeaderValue(
                    from,
                    to,
                    bytes.LongLength);
                paused.Headers.ContentLength = sliceLength;
                response.Content = paused;
            }

            return response;
        }

        private sealed class GateScope(RangeFileHandler owner) : IDisposable
        {
            public void Dispose() => owner._gate?.TrySetResult();
        }
    }

    private sealed class CountingStream(byte[] slice, RangeFileHandler owner) : Stream
    {
        private long _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => slice.LongLength;
        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            owner.EnterStream();
            try
            {
                var count = (int)Math.Min(buffer.Length, slice.LongLength - _offset);
                slice.AsSpan((int)_offset, count).CopyTo(buffer.Span);
                _offset += count;
                return ValueTask.FromResult(count);
            }
            finally
            {
                owner.ExitStream();
            }
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class PausingStream(
        byte[] slice,
        TaskCompletionSource release,
        Action onEnter,
        Action onExit) : Stream
    {
        private bool _firstReadDone;
        private long _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => slice.LongLength;
        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_firstReadDone)
            {
                _firstReadDone = true;
                var count = (int)Math.Min(buffer.Length, slice.LongLength / 2);
                slice.AsSpan((int)_offset, count).CopyTo(buffer.Span);
                _offset += count;
                return count;
            }

            onEnter();
            try
            {
                await release.Task.WaitAsync(cancellationToken);
                var count = (int)Math.Min(buffer.Length, slice.LongLength - _offset);
                slice.AsSpan((int)_offset, count).CopyTo(buffer.Span);
                _offset += count;
                return count;
            }
            finally
            {
                onExit();
            }
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
