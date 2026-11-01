using System.IO;
using System.Text;

namespace HyperDL.Platform;

public static class NativeMessagingFrame
{
    /// <summary>Encodes a JSON payload as a Chrome native-messaging frame.</summary>
    public static byte[] Encode(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var frame = new byte[4 + payload.Length];
        BitConverter.GetBytes(payload.Length).CopyTo(frame, 0);
        payload.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>
    /// Decodes one length-prefixed JSON frame; returns null at end of stream.
    /// </summary>
    public static async Task<string?> DecodeAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var header = new byte[4];
        int read;
        var readTotal = 0;
        while (readTotal < 4 &&
               (read = await stream.ReadAsync(
                    header.AsMemory(readTotal, 4 - readTotal),
                    cancellationToken).ConfigureAwait(false)) > 0)
        {
            readTotal += read;
        }

        if (readTotal < 4)
        {
            return null;
        }

        var length = BitConverter.ToInt32(header, 0);
        if (length is < 0 or > 1024 * 1024)
        {
            return null;
        }

        var payload = new byte[length];
        readTotal = 0;
        while (readTotal < length &&
               (read = await stream.ReadAsync(
                    payload.AsMemory(readTotal, length - readTotal),
                    cancellationToken).ConfigureAwait(false)) > 0)
        {
            readTotal += read;
        }

        if (readTotal < length)
        {
            return null;
        }

        return Encoding.UTF8.GetString(payload);
    }
}

/// <summary>
/// The stdio native-messaging host loop. Messages are JSON objects with a
/// "type" field; the dispatch callback returns the JSON response (or null for
/// no response). The host owns no download state: add requests are forwarded
/// to a running HyperDL instance over the single-instance pipe.
/// </summary>
public static class NativeHost
{
    public const string HostName = "com.ebcsj.hyperdl";

    public static async Task RunAsync(
        Stream input,
        Stream output,
        Func<string, Task<string?>> dispatch,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await NativeMessagingFrame
                .DecodeAsync(input, cancellationToken)
                .ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            var response = await dispatch(request).ConfigureAwait(false);
            if (response is not null)
            {
                var frame = NativeMessagingFrame.Encode(response);
                await output.WriteAsync(frame.AsMemory(), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
