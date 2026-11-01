using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace HyperDL.Platform;

public sealed record UpdateInfo(string Tag, string Url);

/// <summary>
/// Queries the GitHub Releases API for the latest published release. The
/// check is read-only: it never downloads or installs anything, and any
/// failure simply returns null so startup is never disturbed.
/// </summary>
public static class UpdateChecker
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/ebcsj/HyperDL/releases/latest";

    private static readonly HttpClient Client = CreateClient();

    public static async Task<UpdateInfo?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            using var response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            {
                return null;
            }

            var tag = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            var url = root.TryGetProperty("html_url", out var urlElement) &&
                      !string.IsNullOrWhiteSpace(urlElement.GetString())
                ? urlElement.GetString()!
                : "https://github.com/ebcsj/HyperDL/releases";
            return new UpdateInfo(tag, url);
        }
        catch
        {
            // Offline, rate-limited, or blocked: update checks are best effort.
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            UseCookies = false
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
    }
}
