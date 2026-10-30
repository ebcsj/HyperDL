using System.IO;
using System.Net;
using System.Net.Http;

namespace HyperDL.Core;

/// <summary>
/// Retry classification shared by the segmented and single-connection paths.
/// </summary>
public sealed class RetryPolicy
{
    private readonly int _maximumAttempts;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maximumDelay;

    public RetryPolicy(int maximumAttempts, TimeSpan baseDelay, TimeSpan maximumDelay)
    {
        if (maximumAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumAttempts));
        }

        if (baseDelay < TimeSpan.Zero || maximumDelay < baseDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(baseDelay));
        }

        _maximumAttempts = maximumAttempts;
        _baseDelay = baseDelay;
        _maximumDelay = maximumDelay;
    }

    public int MaximumAttempts => _maximumAttempts;

    public bool IsTransientFailure(Exception exception) => IsTransient(exception);

    public static bool IsTransient(Exception exception) => exception switch
    {
        OperationCanceledException => false,
        InvalidDataException => false,
        SourceChangedException => false,
        UnauthorizedAccessException => false,
        InsufficientDiskSpaceException => false,
        FileSystemLimitException => false,
        TransientDownloadException => true,
        TimeoutException => true,
        HttpRequestException http => IsTransientHttp(http),
        IOException => true,
        _ => false
    };

    /// <summary>
    /// Only 408, 429, and 5xx responses are safe to retry. A 403 or 404 will
    /// not become a different answer just because the request is repeated.
    /// </summary>
    private static bool IsTransientHttp(HttpRequestException exception) =>
        exception.StatusCode is null or
            HttpStatusCode.RequestTimeout or
            HttpStatusCode.TooManyRequests or
            >= HttpStatusCode.InternalServerError;

    public TimeSpan GetDelay(int attempt, TransientDownloadException? transient = null)
    {
        if (transient?.RetryAfter is { } retryAfter)
        {
            return Clamp(retryAfter);
        }

        var exponent = Math.Clamp(attempt - 1, 0, 10);
        var milliseconds = _baseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var jitter = Random.Shared.NextDouble() * 0.25 + 0.875;
        return Clamp(TimeSpan.FromMilliseconds(milliseconds * jitter));
    }

    private TimeSpan Clamp(TimeSpan delay) =>
        delay < TimeSpan.Zero
            ? TimeSpan.Zero
            : delay > _maximumDelay
                ? _maximumDelay
                : delay;
}

/// <summary>
/// A failure that is safe to retry (HTTP 429/503/5xx, stalled reads, transient sockets).
/// </summary>
public sealed class TransientDownloadException : IOException
{
    public TransientDownloadException(
        string message,
        HttpStatusCode? statusCode = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public HttpStatusCode? StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}
