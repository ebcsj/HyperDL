using System.Net;

namespace HyperDL.Core;

public enum ByteRangeSupport
{
    Unknown,
    Unsupported,
    Supported
}

public sealed record DownloadMetadata(
    Uri FinalUri,
    HttpStatusCode? StatusCode,
    string FileName,
    string? ContentType,
    long? ContentLength,
    string? ETag,
    DateTimeOffset? LastModified,
    bool AcceptsByteRanges,
    ByteRangeSupport RangeSupport)
{
    public Uri? OriginalUri { get; init; }
}
