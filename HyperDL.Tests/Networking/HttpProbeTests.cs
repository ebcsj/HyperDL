using System.Net;
using System.Net.Http.Headers;
using HyperDL.Core;

namespace HyperDL.Tests;

public class HttpProbeTests
{
    private static readonly Uri ProbeUri = new("https://example.test/archive.bin");

    [Fact]
    public async Task Probe_ValidOneBytePartialContent_ReportsRangeSupport()
    {
        using var client = CreateClient(request =>
        {
            AssertByteZeroRange(request);
            return CreateResponse(
                HttpStatusCode.PartialContent,
                new byte[] { 0x2A },
                new ContentRangeHeaderValue(0, 0, 1000));
        });

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Supported, metadata.RangeSupport);
        Assert.Equal(1000, metadata.ContentLength);
    }

    [Fact]
    public async Task Probe_OkResponseIgnoringRange_ReportsUnsupported()
    {
        using var client = CreateClient(_ =>
            CreateResponse(HttpStatusCode.OK, new byte[] { 1, 2, 3 }));

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_PartialContentWithoutContentRange_ReportsUnsupported()
    {
        using var client = CreateClient(_ =>
            CreateResponse(HttpStatusCode.PartialContent, new byte[] { 1 }));

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_PartialContentWithWrongStartOffset_ReportsUnsupported()
    {
        using var client = CreateClient(_ =>
            CreateResponse(
                HttpStatusCode.PartialContent,
                new byte[] { 1 },
                new ContentRangeHeaderValue(1, 1, 1000)));

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_PartialContentWithWrongEndOffset_ReportsUnsupported()
    {
        using var client = CreateClient(_ =>
            CreateResponse(
                HttpStatusCode.PartialContent,
                new byte[] { 1, 2 },
                new ContentRangeHeaderValue(0, 1, 1000)));

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_PartialContentBodyLongerThanRequestedRange_ReportsUnsupported()
    {
        using var client = CreateClient(_ =>
        {
            var response = CreateResponse(
                HttpStatusCode.PartialContent,
                new byte[] { 1, 2 },
                new ContentRangeHeaderValue(0, 0, 1000));
            response.Content.Headers.ContentLength = null;
            return response;
        });

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_RangeNotSatisfiable_ReportsUnsupportedWithoutRetry()
    {
        var requestCount = 0;
        using var client = CreateClient(_ =>
        {
            requestCount++;
            return CreateResponse(HttpStatusCode.RequestedRangeNotSatisfiable, []);
        });

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
        Assert.Equal(1, requestCount);
    }

    [Fact]
    public async Task Probe_AcceptRangesHeaderWithIgnoredRequest_ReportsUnsupported()
    {
        using var client = CreateClient(_ =>
        {
            var response = CreateResponse(HttpStatusCode.OK, new byte[] { 1, 2, 3 });
            response.Headers.AcceptRanges.Add("bytes");
            return response;
        });

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.True(metadata.AcceptsByteRanges);
        Assert.Equal(ByteRangeSupport.Unsupported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_ValidPartialContentWithoutAcceptRangesHeader_ReportsSupported()
    {
        using var client = CreateClient(_ =>
            CreateResponse(
                HttpStatusCode.PartialContent,
                new byte[] { 1 },
                new ContentRangeHeaderValue(0, 0, 1000)));

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.False(metadata.AcceptsByteRanges);
        Assert.Equal(ByteRangeSupport.Supported, metadata.RangeSupport);
    }

    [Fact]
    public async Task Probe_RequestFailure_ReturnsUnknownMetadata()
    {
        using var client = new HttpClient(new StubHttpMessageHandler(
            (_, _) => throw new HttpRequestException("Simulated network failure.")));

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(ByteRangeSupport.Unknown, metadata.RangeSupport);
        Assert.Null(metadata.StatusCode);
        Assert.Equal("archive.bin", metadata.FileName);
    }

    [Fact]
    public async Task Probe_ContentDispositionFilename_IsSanitized()
    {
        using var client = CreateClient(_ =>
        {
            var response = CreateResponse(
                HttpStatusCode.PartialContent,
                new byte[] { 1 },
                new ContentRangeHeaderValue(0, 0, 1000));
            response.Content.Headers.ContentDisposition =
                ContentDispositionHeaderValue.Parse("attachment; filename=\"../movie.mp4\"");
            return response;
        });

        var metadata = await new HttpProbe(client).ProbeAsync(ProbeUri);

        Assert.Equal(".._movie.mp4", metadata.FileName);
        Assert.Equal(Path.GetFileName(metadata.FileName), metadata.FileName);
    }

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubHttpMessageHandler((request, _) =>
        {
            AssertByteZeroRange(request);
            return Task.FromResult(responder(request));
        }));

    private static void AssertByteZeroRange(HttpRequestMessage request)
    {
        var range = Assert.Single(request.Headers.Range!.Ranges);
        Assert.Equal(0, range.From);
        Assert.Equal(0, range.To);
    }

    private static HttpResponseMessage CreateResponse(
        HttpStatusCode statusCode,
        byte[] body,
        ContentRangeHeaderValue? contentRange = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(body)
        };

        if (contentRange is not null)
        {
            response.Content.Headers.ContentRange = contentRange;
        }

        return response;
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            responder(request, cancellationToken);
    }
}
