using System.Net;
using System.Net.Http.Headers;
using HyperDL.Core;

namespace HyperDL.Tests;

public class DownloadEngineSegmentTests
{
    private static readonly Uri DownloadUri = new("https://example.test/archive.bin");

    [Fact]
    public async Task DownloadAsync_FourSegmentsUseRequestedInclusiveRangesAndReconstructFile()
    {
        var bytes = CreatePatternedBytes(checked((int)(4 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(request => CreateValidRangeResponse(request, bytes));
        var progressReports = new List<DownloadProgress>();
        var progressLock = new object();
        var progress = new InlineProgress<DownloadProgress>(update =>
        {
            lock (progressLock)
            {
                progressReports.Add(update);
            }
        });

        var outputPath = await context.Engine.DownloadAsync(
            CreateMetadata(bytes.LongLength),
            progress,
            requestedConnectionCount: 4);

        var requestRanges = context.Handler.Requests
            .Select(request => request.Headers.Range!.Ranges.Single())
            .OrderBy(range => range.From)
            .ToArray();
        Assert.Equal(4, requestRanges.Length);
        Assert.Equal(0, requestRanges[0].From);
        Assert.Equal(bytes.LongLength - 1, requestRanges[^1].To);
        for (var index = 1; index < requestRanges.Length; index++)
        {
            Assert.Equal(requestRanges[index - 1].To + 1, requestRanges[index].From);
        }

        Assert.Equal(bytes, await File.ReadAllBytesAsync(outputPath));
        Assert.False(File.Exists(outputPath + ".part"));
        Assert.Contains(progressReports, report =>
            report.SegmentCount == 4 &&
            report.Segments?.All(segment => segment.Status == DownloadSegmentStatus.Completed) == true);
        Assert.Contains(progressReports, report =>
            report.BytesDownloaded == bytes.LongLength &&
            report.Percentage == 100);
    }

    [Fact]
    public async Task DownloadAsync_FourSegmentsHandleUnevenFileSize()
    {
        var bytes = CreatePatternedBytes(checked((int)(4 * DownloadEngine.MinimumSegmentSize + 3)));
        using var context = CreateContext(request => CreateValidRangeResponse(request, bytes));

        var outputPath = await context.Engine.DownloadAsync(
            CreateMetadata(bytes.LongLength),
            requestedConnectionCount: 4);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(outputPath));
        Assert.Equal(bytes.LongLength, new FileInfo(outputPath).Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    public void CreateSession_UsesSelectedConnectionCountAndContinuousRanges(
        int requestedConnectionCount)
    {
        using var context = CreateContext(null);
        var metadata = CreateMetadata(64 * DownloadEngine.MinimumSegmentSize);

        var session = context.Engine.CreateSession(metadata, requestedConnectionCount);

        Assert.Equal(requestedConnectionCount, session.ConnectionCount);
        Assert.Equal(requestedConnectionCount, session.Segments.Count);
        Assert.Equal(0, session.Segments[0].StartOffset);
        Assert.Equal(metadata.ContentLength!.Value - 1, session.Segments[^1].EndOffset);
        for (var index = 1; index < session.Segments.Count; index++)
        {
            Assert.Equal(
                session.Segments[index - 1].EndOffset + 1,
                session.Segments[index].StartOffset);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task DownloadAsync_SmallFileUsesSingleConnectionWithoutEmptySegments(int byteCount)
    {
        var bytes = CreatePatternedBytes(byteCount);
        using var context = CreateContext(request =>
        {
            Assert.Null(request.Headers.Range);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        });
        var metadata = CreateMetadata(bytes.LongLength) with
        {
            RangeSupport = ByteRangeSupport.Unsupported
        };

        var outputPath = await context.Engine.DownloadAsync(metadata);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(outputPath));
        Assert.Single(context.Handler.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DownloadAsync_InvalidSegmentResponseFailsWithoutFinalFile(int failingSegment)
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            if (range.From == (long)failingSegment * DownloadEngine.MinimumSegmentSize)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
            }

            return CreateValidRangeResponse(request, bytes);
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Engine.DownloadAsync(CreateMetadata(bytes.LongLength)));

        Assert.False(File.Exists(Path.Combine(context.DirectoryPath, "archive.bin")));
        Assert.False(File.Exists(Path.Combine(context.DirectoryPath, "archive.bin.part")));
    }

    [Fact]
    public async Task DownloadAsync_WrongContentRangeFailsWithoutFinalFile()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var response = CreateValidRangeResponse(request, bytes);
            if (range.From == 0)
            {
                response.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(1, range.To!.Value, bytes.LongLength);
            }

            return response;
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Engine.DownloadAsync(CreateMetadata(bytes.LongLength)));

        AssertNoFinalOrPartFile(context);
    }

    [Fact]
    public async Task DownloadAsync_ShortSegmentBodyFailsWithoutFinalFile()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var response = CreateValidRangeResponse(request, bytes);
            if (range.From == 0)
            {
                var shortBody = SliceRange(bytes, range)[..^1];
                response.Content.Dispose();
                response.Content = new ByteArrayContent(shortBody);
                response.Content.Headers.ContentLength = null;
                response.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, bytes.LongLength);
            }

            return response;
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Engine.DownloadAsync(CreateMetadata(bytes.LongLength)));

        AssertNoFinalOrPartFile(context);
    }

    [Fact]
    public async Task DownloadAsync_LongSegmentBodyFailsWithoutFinalFile()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(request =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var response = CreateValidRangeResponse(request, bytes);
            if (range.From == 0)
            {
                var longBody = SliceRange(bytes, range).Concat(new byte[] { 0xFF }).ToArray();
                response.Content.Dispose();
                response.Content = new ByteArrayContent(longBody);
                response.Content.Headers.ContentLength = null;
                response.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(range.From!.Value, range.To!.Value, bytes.LongLength);
            }

            return response;
        });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Engine.DownloadAsync(CreateMetadata(bytes.LongLength)));

        AssertNoFinalOrPartFile(context);
    }

    [Fact]
    public async Task DownloadAsync_SegmentFailureCancelsOtherWorkersAndIsObserved()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(null);
        var requestsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        context.Handler.Responder = async (request, cancellationToken) =>
        {
            var entered = Interlocked.Increment(ref enteredCount);
            if (entered == 2)
            {
                requestsEntered.TrySetResult();
            }

            await requestsEntered.Task.WaitAsync(cancellationToken);
            if (request.Headers.Range!.Ranges.Single().From == 0)
            {
                throw new HttpRequestException("Simulated segment failure.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            return CreateValidRangeResponse(request, bytes);
        };

        await Assert.ThrowsAsync<HttpRequestException>(
            () => context.Engine.DownloadAsync(CreateMetadata(bytes.LongLength)));

        Assert.Equal(2, enteredCount);
        AssertNoFinalOrPartFile(context);
    }

    [Fact]
    public async Task DownloadAsync_UserCancellationDoesNotCreateFinalFile()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(null);
        using var cancellation = new CancellationTokenSource();
        var requestsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        context.Handler.Responder = async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref enteredCount) == 2)
            {
                requestsEntered.TrySetResult();
            }

            await requestsEntered.Task.WaitAsync(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        };

        var downloadTask = context.Engine.DownloadAsync(
            CreateMetadata(bytes.LongLength),
            cancellationToken: cancellation.Token);
        await requestsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloadTask);
        AssertNoFinalOrPartFile(context);
    }

    [Fact]
    public async Task DownloadAsync_PartFileIsNotRenamedUntilEverySegmentCompletes()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(null);
        var requestsEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponses = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        context.Handler.Responder = async (request, cancellationToken) =>
        {
            if (Interlocked.Increment(ref enteredCount) == 2)
            {
                requestsEntered.TrySetResult();
            }

            await releaseResponses.Task.WaitAsync(cancellationToken);
            return CreateValidRangeResponse(request, bytes);
        };

        var downloadTask = context.Engine.DownloadAsync(CreateMetadata(bytes.LongLength));
        await requestsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var finalPath = Path.Combine(context.DirectoryPath, "archive.bin");
        Assert.False(File.Exists(finalPath));
        Assert.True(File.Exists(finalPath + ".part"));

        releaseResponses.TrySetResult();
        var completedPath = await downloadTask;

        Assert.Equal(finalPath, completedPath);
        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(finalPath + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_UnsupportedRangeRetainsSingleConnectionFallback()
    {
        var bytes = CreatePatternedBytes(128);
        using var context = CreateContext(request =>
        {
            Assert.Null(request.Headers.Range);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
        });
        var metadata = CreateMetadata(bytes.LongLength) with
        {
            RangeSupport = ByteRangeSupport.Unsupported
        };

        var outputPath = await context.Engine.DownloadAsync(metadata);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(outputPath));
        Assert.Single(context.Handler.Requests);
        Assert.Null(context.Handler.Requests[0].Headers.Range);
    }

    [Fact]
    public async Task DownloadAsync_SingleConnectionWritesPartUntilFinalValidation()
    {
        var bytes = CreatePatternedBytes(4096);
        using var context = CreateContext(null);
        var secondReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecondRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Handler.Responder = (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(
                    new GatedStream(bytes, secondReadStarted, releaseSecondRead))
            };
            response.Content.Headers.ContentLength = bytes.LongLength;
            return Task.FromResult(response);
        };

        var downloadTask = context.Engine.DownloadAsync(DownloadUri);
        await secondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var finalPath = Path.Combine(context.DirectoryPath, "archive.bin");
        Assert.False(File.Exists(finalPath));
        Assert.True(File.Exists(finalPath + ".part"));

        releaseSecondRead.TrySetResult();
        var outputPath = await downloadTask;

        Assert.Equal(finalPath, outputPath);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(finalPath));
        Assert.False(File.Exists(finalPath + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_PauseRetainsOffsetsAndResumeRequestsOnlyRemainingBytes()
    {
        var bytes = CreatePatternedBytes(checked((int)(2 * DownloadEngine.MinimumSegmentSize)));
        using var context = CreateContext(null);
        var secondReadStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        context.Handler.Responder = (request, _) =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new StreamContent(
                    new PauseAfterFirstReadStream(
                        SliceRange(bytes, range),
                        secondReadStarted))
            };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
                range.From!.Value,
                range.To!.Value,
                bytes.LongLength);
            response.Content.Headers.ContentLength = range.To.Value - range.From.Value + 1;
            return Task.FromResult(response);
        };

        var session = context.Engine.CreateSession(CreateMetadata(bytes.LongLength), 2);
        var pauseTask = context.Engine.DownloadAsync(session);
        await secondReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        session.Pause();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pauseTask);
        Assert.Equal(DownloadSessionStatus.Paused, session.Status);
        Assert.True(File.Exists(session.PartPath));
        Assert.InRange(
            session.Segments.Sum(segment => segment.BytesDownloaded),
            1,
            bytes.LongLength - 1);

        var initialOffsets = session.Segments.ToDictionary(
            segment => segment.Index,
            segment => segment.CurrentOffset);
        context.Handler.Responder = (request, _) =>
            Task.FromResult(CreateValidRangeResponse(request, bytes));

        var finalPath = await context.Engine.DownloadAsync(session);

        Assert.Equal(DownloadSessionStatus.Completed, session.Status);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(finalPath));
        Assert.All(
            context.Handler.Requests.Skip(2),
            request =>
            {
                var range = request.Headers.Range!.Ranges.Single();
                var index = session.Segments.Single(segment =>
                    segment.StartOffset <= range.From &&
                    segment.EndOffset >= range.From).Index;
                Assert.Equal(initialOffsets[index], range.From);
            });
        Assert.False(File.Exists(session.PartPath));
    }

    [Fact]
    public void DownloadSession_RejectsChangedRemoteFileIdentity()
    {
        using var context = CreateContext(null);
        var session = context.Engine.CreateSession(
            CreateMetadata(2 * DownloadEngine.MinimumSegmentSize),
            2);
        var changed = session.Metadata with { ETag = "\"changed\"" };

        Assert.Throws<InvalidOperationException>(
            () => session.EnsureSourceUnchanged(changed));
    }

    [Theory]
    [InlineData(DownloadEngine.MinimumSegmentSize - 1, 1)]
    [InlineData(2 * DownloadEngine.MinimumSegmentSize, 2)]
    [InlineData(3 * DownloadEngine.MinimumSegmentSize, 2)]
    [InlineData(4 * DownloadEngine.MinimumSegmentSize, 2)]
    [InlineData(20 * DownloadEngine.MinimumSegmentSize, 4)]
    [InlineData(32 * DownloadEngine.MinimumSegmentSize, 8)]
    [InlineData(256 * DownloadEngine.MinimumSegmentSize, 16)]
    [InlineData(2048 * DownloadEngine.MinimumSegmentSize, 32)]
    [InlineData(8192 * DownloadEngine.MinimumSegmentSize, 64)]
    public void GetSegmentCount_AutoSelectsReasonablySizedSegments(long fileSize, int expectedCount)
    {
        Assert.Equal(expectedCount, DownloadEngine.GetSegmentCount(CreateMetadata(fileSize)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(32, 32)]
    [InlineData(64, 64)]
    public void GetSegmentCount_HonorsSupportedConnectionCount(int requested, int expected)
    {
        var fileSize = 256 * DownloadEngine.MinimumSegmentSize;
        Assert.Equal(
            expected,
            DownloadEngine.GetSegmentCount(CreateMetadata(fileSize), requested));
    }

    [Fact]
    public void GetSegmentCount_UsesLargestSupportedCountThatFitsMinimumSegmentSize()
    {
        var metadata = CreateMetadata(5 * DownloadEngine.MinimumSegmentSize);

        Assert.Equal(4, DownloadEngine.GetSegmentCount(metadata, 16));
    }

    [Fact]
    public void GetSegmentCount_RejectsUnsupportedConnectionCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DownloadEngine.GetSegmentCount(
                CreateMetadata(16 * DownloadEngine.MinimumSegmentSize),
                3));
    }

    [Theory]
    [InlineData(false, DownloadEngine.BalancedBufferSize)]
    [InlineData(true, DownloadEngine.LowRamBufferSize)]
    public void DownloadEngine_UsesBoundedBufferForSelectedRamMode(
        bool lowRamMode,
        int expectedBufferSize)
    {
        using var context = CreateContext(null, lowRamMode);
        Assert.Equal(expectedBufferSize, context.Engine.BufferSize);
        Assert.InRange(context.Engine.BufferSize, 1, DownloadEngine.BalancedBufferSize);
        Assert.True(
            (long)DownloadEngine.MaximumSegmentCount * context.Engine.BufferSize <=
            DownloadEngine.GlobalNetworkBufferBudgetBytes);
    }

    [Theory]
    [InlineData(ByteRangeSupport.Unknown, 16 * DownloadEngine.MinimumSegmentSize)]
    [InlineData(ByteRangeSupport.Unsupported, 16 * DownloadEngine.MinimumSegmentSize)]
    [InlineData(ByteRangeSupport.Supported, 0L)]
    [InlineData(ByteRangeSupport.Supported, null)]
    public void GetSegmentCount_UsesSingleConnectionWithoutConfirmedRangeAndSize(
        ByteRangeSupport rangeSupport,
        long? fileSize)
    {
        var metadata = CreateMetadata(fileSize ?? 0) with
        {
            RangeSupport = rangeSupport,
            ContentLength = fileSize
        };

        Assert.Equal(1, DownloadEngine.GetSegmentCount(metadata));
    }

    [Fact]
    public void GetSegmentCount_RejectsNonHttpMetadataUri()
    {
        var metadata = CreateMetadata(16 * DownloadEngine.MinimumSegmentSize) with
        {
            FinalUri = new Uri("ftp://example.test/archive.bin")
        };

        Assert.Equal(1, DownloadEngine.GetSegmentCount(metadata));
    }

    private static DownloadMetadata CreateMetadata(long fileSize) =>
        new(
            DownloadUri,
            HttpStatusCode.PartialContent,
            "archive.bin",
            "application/octet-stream",
            fileSize,
            "\"test-etag\"",
            null,
            true,
            ByteRangeSupport.Supported);

    private static byte[] CreatePatternedBytes(int length)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index * 31);
        }

        return bytes;
    }

    private static HttpResponseMessage CreateValidRangeResponse(
        HttpRequestMessage request,
        byte[] sourceBytes)
    {
        var range = request.Headers.Range!.Ranges.Single();
        var body = SliceRange(sourceBytes, range);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(body)
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(
            range.From!.Value,
            range.To!.Value,
            sourceBytes.LongLength);
        return response;
    }

    private static byte[] SliceRange(byte[] source, RangeItemHeaderValue range)
    {
        var start = checked((int)range.From!.Value);
        var length = checked((int)(range.To!.Value - range.From.Value + 1));
        return source.AsSpan(start, length).ToArray();
    }

    private static void AssertNoFinalOrPartFile(DownloadTestContext context)
    {
        Assert.False(File.Exists(Path.Combine(context.DirectoryPath, "archive.bin")));
        Assert.False(File.Exists(Path.Combine(context.DirectoryPath, "archive.bin.part")));
    }

    private static DownloadTestContext CreateContext(
        Func<HttpRequestMessage, HttpResponseMessage>? responder,
        bool lowRamMode = false) =>
        new(
            responder is null ? null : (request, _) => Task.FromResult(responder(request)),
            lowRamMode);

    private sealed class DownloadTestContext : IDisposable
    {
        public DownloadTestContext(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? responder,
            bool lowRamMode = false)
        {
            DirectoryPath = Path.Combine(
                Path.GetTempPath(),
                $"HyperDL.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            Handler = new StubHandler(responder ?? ((_, _) =>
                throw new InvalidOperationException("No test response was configured.")));
            Client = new HttpClient(Handler);
            Engine = new DownloadEngine(Client, DirectoryPath, lowRamMode);
        }

        public string DirectoryPath { get; }
        public StubHandler Handler { get; }
        public HttpClient Client { get; }
        public DownloadEngine Engine { get; }

        public void Dispose()
        {
            Client.Dispose();
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> _requests = [];
        private readonly object _requestsLock = new();

        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Responder
        {
            get;
            set;
        } = responder;

        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get
            {
                lock (_requestsLock)
                {
                    return _requests.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            lock (_requestsLock)
            {
                _requests.Add(request);
            }

            return Responder(request, cancellationToken);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class PauseAfterFirstReadStream(
        byte[] content,
        TaskCompletionSource secondReadStarted) : Stream
    {
        private int _readCount;
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) > 1)
            {
                secondReadStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            var count = Math.Min(buffer.Length, content.Length - _offset);
            content.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }
    }

    private sealed class GatedStream(
        byte[] content,
        TaskCompletionSource secondReadStarted,
        TaskCompletionSource releaseSecondRead) : Stream
    {
        private int _readCount;
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => content.Length;
        public override long Position
        {
            get => _offset;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _readCount) == 2)
            {
                secondReadStarted.TrySetResult();
                await releaseSecondRead.Task.WaitAsync(cancellationToken);
            }

            var maxCount = _readCount == 1
                ? Math.Max(1, content.Length / 2)
                : content.Length - _offset;
            var count = Math.Min(buffer.Length, Math.Min(maxCount, content.Length - _offset));
            content.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }
    }
}
