using System.IO;
using System.Net;
using System.Net.Http;

namespace HyperDL.Core;

public enum DownloadErrorKind
{
    Network,
    Timeout,
    NotFound,
    Forbidden,
    RateLimited,
    Server,
    DiskFull,
    FileSystemLimit,
    Permission,
    SourceChanged,
    InvalidData,
    Cancelled,
    Unknown
}

/// <summary>
/// Maps low-level failures to a small set of user-facing categories. The UI owns
/// the localized text so the engine stays culture neutral.
/// </summary>
public static class DownloadErrorClassifier
{
    public static DownloadErrorKind Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            SourceChangedException => DownloadErrorKind.SourceChanged,
            OperationCanceledException => DownloadErrorKind.Cancelled,
            TransientDownloadException transient => ClassifyStatus(transient.StatusCode),
            TimeoutException => DownloadErrorKind.Timeout,
            UnauthorizedAccessException => DownloadErrorKind.Permission,
            FileSystemLimitException => DownloadErrorKind.FileSystemLimit,
            InsufficientDiskSpaceException => DownloadErrorKind.DiskFull,
            HttpRequestException http => ClassifyHttp(http),
            IOException io when IsDiskFull(io) => DownloadErrorKind.DiskFull,
            IOException => DownloadErrorKind.Network,
            InvalidDataException => DownloadErrorKind.InvalidData,
            _ => DownloadErrorKind.Unknown
        };
    }

    public static string ToResourceKey(this DownloadErrorKind kind) => kind switch
    {
        DownloadErrorKind.Network => "ErrorNetwork",
        DownloadErrorKind.Timeout => "ErrorTimeout",
        DownloadErrorKind.NotFound => "ErrorNotFound",
        DownloadErrorKind.Forbidden => "ErrorForbidden",
        DownloadErrorKind.RateLimited => "ErrorRateLimited",
        DownloadErrorKind.Server => "ErrorServer",
        DownloadErrorKind.DiskFull => "ErrorDiskFull",
        DownloadErrorKind.FileSystemLimit => "ErrorFileSystemLimit",
        DownloadErrorKind.Permission => "ErrorPermission",
        DownloadErrorKind.SourceChanged => "ErrorSourceChanged",
        DownloadErrorKind.InvalidData => "ErrorInvalidData",
        DownloadErrorKind.Cancelled => "ErrorCancelled",
        _ => "ErrorUnknown"
    };

    private static DownloadErrorKind ClassifyHttp(HttpRequestException exception)
    {
        if (exception.StatusCode is { } statusCode)
        {
            return ClassifyStatus(statusCode);
        }

        return exception.InnerException is TimeoutException
            ? DownloadErrorKind.Timeout
            : DownloadErrorKind.Network;
    }

    private static DownloadErrorKind ClassifyStatus(HttpStatusCode? statusCode) =>
        statusCode switch
        {
            HttpStatusCode.NotFound => DownloadErrorKind.NotFound,
            HttpStatusCode.Forbidden => DownloadErrorKind.Forbidden,
            HttpStatusCode.Unauthorized => DownloadErrorKind.Forbidden,
            HttpStatusCode.TooManyRequests => DownloadErrorKind.RateLimited,
            HttpStatusCode.RequestTimeout => DownloadErrorKind.Timeout,
            >= HttpStatusCode.InternalServerError => DownloadErrorKind.Server,
            null => DownloadErrorKind.Network,
            _ => DownloadErrorKind.Network
        };

    private static bool IsDiskFull(IOException exception)
    {
        const int DiskFull = unchecked((int)0x80070070);
        const int HandleDiskFull = unchecked((int)0x80070027);
        const int DiskQuota = unchecked((int)0x80070027);
        return exception.HResult is DiskFull or HandleDiskFull or DiskQuota;
    }
}

/// <summary>
/// Raised when the remote representation no longer matches the stored metadata.
/// </summary>
public sealed class SourceChangedException : InvalidOperationException
{
    public SourceChangedException(string message) : base(message)
    {
    }
}
