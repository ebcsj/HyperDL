using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace HyperDL.Core;

public sealed class HttpProbe
{
    private static readonly HttpClient Client = CreateHttpClient();
    private readonly HttpClient _client;

    public HttpProbe(HttpClient? client = null)
    {
        _client = client ?? Client;
    }

    public async Task<DownloadMetadata> ProbeAsync(
        Uri uri,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (!DownloadUrlValidator.IsSupported(uri))
        {
            throw new ArgumentException("Only HTTP and HTTPS URLs are supported.", nameof(uri));
        }

        using var probeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
        var probeToken = probeTimeout.Token;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(0, 0);

            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                probeToken);

            var finalUri = response.RequestMessage?.RequestUri ?? uri;
            var metadata = CreateMetadata(
                finalUri,
                response.StatusCode,
                response.Content.Headers.ContentDisposition,
                response.Content.Headers.ContentType?.ToString(),
                GetContentLength(response),
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified,
                response.Headers.AcceptRanges.Any(range =>
                    string.Equals(range, "bytes", StringComparison.OrdinalIgnoreCase)),
                ByteRangeSupport.Unknown);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return metadata with
                {
                    RangeSupport = ByteRangeSupport.Unsupported,
                    OriginalUri = uri
                };
            }

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                return metadata with
                {
                    RangeSupport = ByteRangeSupport.Unsupported,
                    OriginalUri = uri
                };
            }

            var contentRange = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent)
            {
                return metadata with { OriginalUri = uri };
            }

            if (
                contentRange is null ||
                !string.Equals(contentRange.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
                contentRange.From != 0 ||
                contentRange.To != 0 ||
                (contentRange.Length.HasValue && contentRange.Length.Value <= contentRange.To) ||
                (response.Content.Headers.ContentLength.HasValue &&
                 response.Content.Headers.ContentLength.Value != 1))
            {
                return metadata with
                {
                    RangeSupport = ByteRangeSupport.Unsupported,
                    OriginalUri = uri
                };
            }

            var probeBytes = new byte[2];
            await using var probeStream = await response.Content.ReadAsStreamAsync(probeToken);
            var bytesRead = 0;
            while (bytesRead < probeBytes.Length)
            {
                var count = await probeStream.ReadAsync(
                    probeBytes.AsMemory(bytesRead),
                    probeToken);
                if (count == 0)
                {
                    break;
                }

                bytesRead += count;
            }

            if (bytesRead != 1)
            {
                return metadata with { RangeSupport = ByteRangeSupport.Unsupported };
            }

            return metadata with
            {
                ContentLength = contentRange.Length ?? metadata.ContentLength,
                RangeSupport = ByteRangeSupport.Supported,
                OriginalUri = uri
            };
        }
        catch (HttpRequestException)
        {
            return CreateUnknownMetadata(uri);
        }
        catch (IOException)
        {
            return CreateUnknownMetadata(uri);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateUnknownMetadata(uri);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            MaxConnectionsPerServer = 1
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    private static DownloadMetadata CreateUnknownMetadata(Uri uri) =>
        CreateMetadata(uri, null, null, null, null, null, null, false, ByteRangeSupport.Unknown);

    private static DownloadMetadata CreateMetadata(
        Uri finalUri,
        HttpStatusCode? statusCode,
        ContentDispositionHeaderValue? contentDisposition,
        string? contentType,
        long? contentLength,
        string? etag,
        DateTimeOffset? lastModified,
        bool acceptsByteRanges,
        ByteRangeSupport rangeSupport)
    {
        var headerFileName = contentDisposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(headerFileName))
        {
            headerFileName = contentDisposition?.FileName;
        }

        var candidateFileName = string.IsNullOrWhiteSpace(headerFileName)
            ? GetFileNameFromUri(finalUri)
            : Uri.UnescapeDataString(headerFileName.Trim().Trim('"'));

        return new DownloadMetadata(
            finalUri,
            statusCode,
            FileNameSanitizer.Sanitize(candidateFileName),
            contentType,
            contentLength,
            etag,
            lastModified,
            acceptsByteRanges,
            rangeSupport);
    }

    private static long? GetContentLength(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.PartialContent or
            HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            return response.Content.Headers.ContentRange?.Length;
        }

        return response.IsSuccessStatusCode
            ? response.Content.Headers.ContentLength
            : null;
    }

    private static string GetFileNameFromUri(Uri uri)
    {
        var lastSegment = uri.Segments.Length > 0
            ? Uri.UnescapeDataString(uri.Segments[^1]).Trim('/')
            : string.Empty;

        return string.IsNullOrWhiteSpace(lastSegment) ? "download" : lastSegment;
    }

}
