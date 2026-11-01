using System.IO;
using System.Net.Http;
using System.Text;
using HyperDL.Core;

namespace HyperDL.Platform;

public sealed record DownloadedUpdate(
    string InstallerPath,
    string ExpectedSha256,
    string ActualSha256,
    bool Verified);

/// <summary>
/// Downloads a release installer and verifies it against the release's
/// SHA256SUMS.txt. Verification failure discards the file: a corrupted or
/// tampered installer is never executed.
/// </summary>
public static class UpdateInstaller
{
    private static readonly HttpClient Client = CreateClient();

    /// <summary>Finds the expected hash for a file inside SHA256SUMS.txt content.</summary>
    public static string? ParseSums(string content, string fileName)
    {
        foreach (var line in content.Split('\n'))
        {
            var parts = line.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 &&
                parts[1].Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return parts[0];
            }
        }

        return null;
    }

    public static async Task<DownloadedUpdate?> DownloadAndVerifyAsync(
        UpdateInfo info,
        string destinationDirectory,
        IProgress<double>? progress,
        CancellationToken cancellationToken = default)
    {
        if (info.InstallerUrl is null || info.SumsUrl is null)
        {
            return null;
        }

        Directory.CreateDirectory(destinationDirectory);
        var installerName = Uri.UnescapeDataString(
            new Uri(info.InstallerUrl).Segments[^1]);
        var installerPath = Path.Combine(destinationDirectory, installerName);

        await DownloadFileAsync(info.InstallerUrl, installerPath, progress, cancellationToken)
            .ConfigureAwait(false);

        var sumsPath = Path.Combine(destinationDirectory, "SHA256SUMS.txt");
        await DownloadFileAsync(info.SumsUrl, sumsPath, null, cancellationToken)
            .ConfigureAwait(false);
        var expected = ParseSums(await File.ReadAllTextAsync(sumsPath, cancellationToken)
            .ConfigureAwait(false), installerName);
        if (expected is null)
        {
            File.Delete(installerPath);
            return null;
        }

        var actual = await FileHash.ComputeSha256Async(installerPath, cancellationToken)
            .ConfigureAwait(false);
        var verified = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        if (!verified)
        {
            File.Delete(installerPath);
            return null;
        }

        return new DownloadedUpdate(installerPath, expected, actual, verified);
    }

    private static async Task DownloadFileAsync(
        string url,
        string destinationPath,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var target = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[64 * 1024];
        long written = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
            if (progress is not null && total is > 0)
            {
                progress.Report(written * 100d / total.Value);
            }
        }

        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            UseCookies = false
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(10) };
    }
}
