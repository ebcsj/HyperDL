namespace HyperDL.Core;

public static class DownloadUrlValidator
{
    public static bool IsSupported(Uri? uri) =>
        uri is { IsAbsoluteUri: true } &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
