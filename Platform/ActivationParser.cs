using HyperDL.Core;

namespace HyperDL.Platform;

/// <summary>
/// A parsed startup or single-instance IPC activation: URLs to add, whether
/// the activation came from an external source, and optional per-request
/// overrides carried by the hyperdl:// protocol.
/// </summary>
public sealed record ActivationRequest(
    IReadOnlyList<Uri> Uris,
    bool External,
    string? InvalidText,
    int? Connections,
    bool? Confirm);

/// <summary>
/// Parses command-line and pipe payloads into <see cref="ActivationRequest"/>.
/// Only HTTP/HTTPS URLs are accepted. The hyperdl:// protocol may carry
/// connections (0 = auto, otherwise a supported connection count) and a
/// confirm hint; both are advisory overrides the app validates again.
/// </summary>
public static class ActivationParser
{
    private static readonly int[] SupportedConnectionCounts = [0, 1, 2, 4, 8, 16, 32, 64];

    public static ActivationRequest Parse(string? payload)
    {
        var uris = new List<Uri>();
        var invalid = new List<string>();
        var external = false;
        int? connections = null;
        bool? confirm = null;

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new ActivationRequest(uris, false, null, null, null);
        }

        var tokens = payload.Split(
            ['\r', '\n', '\t', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            var token = tokens[index].Trim().Trim('"', '\'', '<', '>');
            if (token.StartsWith("hyperdl:", StringComparison.OrdinalIgnoreCase))
            {
                external = true;
                var target = ParseProtocolUrl(token, out var protocolConnections, out var protocolConfirm);
                if (target is not null)
                {
                    uris.Add(target);
                    connections ??= protocolConnections;
                    confirm ??= protocolConfirm;
                }
                else
                {
                    invalid.Add(token);
                }

                continue;
            }

            if ((token.Equals("--add", StringComparison.OrdinalIgnoreCase) ||
                 token.Equals("--url", StringComparison.OrdinalIgnoreCase)) &&
                index + 1 < tokens.Length)
            {
                token = tokens[++index].Trim('"', '\'', '<', '>');
            }

            if (Uri.TryCreate(token, UriKind.Absolute, out var uri) &&
                DownloadUrlValidator.IsSupported(uri))
            {
                uris.Add(uri);
            }
            else if (token.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                invalid.Add(token);
            }
        }

        var distinct = uris
            .DistinctBy(uri => uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped))
            .ToArray();
        return new ActivationRequest(
            distinct,
            external,
            invalid.Count > 0 ? string.Join(' ', invalid) : null,
            connections,
            confirm);
    }

    private static Uri? ParseProtocolUrl(string value, out int? connections, out bool? confirm)
    {
        connections = null;
        confirm = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(ProtocolRegistration.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var queryUrl = GetQueryParameter(uri, "url");
        Uri? result = null;
        if (queryUrl is not null &&
            Uri.TryCreate(queryUrl, UriKind.Absolute, out var fromQuery) &&
            DownloadUrlValidator.IsSupported(fromQuery))
        {
            result = fromQuery;
        }
        else
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
            if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(path, UriKind.Absolute, out var fromPath) &&
                DownloadUrlValidator.IsSupported(fromPath))
            {
                result = fromPath;
            }
        }

        if (result is null)
        {
            return null;
        }

        var connectionsText = GetQueryParameter(uri, "connections");
        if (connectionsText is not null &&
            int.TryParse(connectionsText, out var parsedConnections) &&
            SupportedConnectionCounts.Contains(parsedConnections))
        {
            connections = parsedConnections;
        }

        var confirmText = GetQueryParameter(uri, "confirm");
        if (confirmText == "0")
        {
            confirm = false;
        }
        else if (confirmText == "1")
        {
            confirm = true;
        }

        return result;
    }

    private static string? GetQueryParameter(Uri uri, string name)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator >= 0 ? part[..separator] : part;
            if (!Uri.UnescapeDataString(key).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = separator >= 0 ? part[(separator + 1)..] : string.Empty;
            return Uri.UnescapeDataString(raw.Replace('+', ' '));
        }

        return null;
    }
}
