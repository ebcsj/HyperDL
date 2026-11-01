using System.Text.Json;

namespace HyperDL.Platform;

public sealed record IpcRequest(string Command, IReadOnlyList<string> Urls, int? Connections);

public sealed record IpcJobSnapshot(
    Guid Id,
    string FileName,
    string Status,
    double Percentage,
    long BytesDownloaded,
    long? TotalBytes,
    string? TargetPath,
    string? Sha256);

public sealed record IpcResponse(
    bool Ok,
    string? Error,
    IReadOnlyList<IpcJobSnapshot>? Jobs,
    int? Added);

/// <summary>
/// JSON framing for the single-instance pipe. Commands arrive as one JSON
/// line; legacy clients (raw URL text) are detected by the absence of a
/// leading brace and handled as activations.
/// </summary>
public static class IpcProtocol
{
    public static bool LooksLikeRequest(string line) =>
        line.TrimStart().StartsWith('{');

    public static IpcRequest? ParseRequest(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("cmd", out var cmdElement) ||
                cmdElement.GetString() is not { } command)
            {
                return null;
            }

            var urls = new List<string>();
            if (root.TryGetProperty("urls", out var urlsElement) &&
                urlsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in urlsElement.EnumerateArray())
                {
                    if (item.GetString() is { } url)
                    {
                        urls.Add(url);
                    }
                }
            }
            else if (root.TryGetProperty("url", out var urlElement) &&
                     urlElement.GetString() is { } single)
            {
                urls.Add(single);
            }

            int? connections = null;
            if (root.TryGetProperty("connections", out var connectionsElement) &&
                connectionsElement.TryGetInt32(out var parsed))
            {
                connections = parsed;
            }

            return new IpcRequest(command.Trim().ToLowerInvariant(), urls, connections);
        }
        catch
        {
            return null;
        }
    }

    public static string BuildResponse(IpcResponse response)
    {
        var jobs = response.Jobs is null
            ? null
            : response.Jobs.Select(job => new
            {
                id = job.Id,
                fileName = job.FileName,
                status = job.Status,
                percentage = job.Percentage,
                bytesDownloaded = job.BytesDownloaded,
                totalBytes = job.TotalBytes,
                targetPath = job.TargetPath,
                sha256 = job.Sha256
            });
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        return JsonSerializer.Serialize(
            new { ok = response.Ok, error = response.Error, jobs, added = response.Added },
            options);
    }

    /// <summary>
    /// Builds the JSON request line for a CLI command. Add/import commands
    /// carry their URL list; control commands carry only the name.
    /// </summary>
    public static string BuildRequest(string command, IReadOnlyList<string>? urls = null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        return JsonSerializer.Serialize(
            new { cmd = command.ToLowerInvariant(), urls = urls ?? Array.Empty<string>() },
            options);
    }
}
