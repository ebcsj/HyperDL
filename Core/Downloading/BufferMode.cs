namespace HyperDL.Core;

/// <summary>
/// Controls how much memory each active connection may use for its transfer buffer.
/// All modes stay inside <see cref="DownloadEngine.GlobalNetworkBufferBudgetBytes"/>.
/// </summary>
public enum BufferMode
{
    Auto,
    LowRam,
    Balanced,
    Fast
}

public static class BufferModeSizing
{
    /// <summary>
    /// Resolves the per-connection buffer size while never exceeding the global budget
    /// for the supplied number of active connections.
    /// </summary>
    public static int ResolveBufferSize(this BufferMode mode, int connectionCount)
    {
        var connections = Math.Clamp(connectionCount, 1, DownloadEngine.MaximumSegmentCount);
        var budgetPerConnection = (int)(DownloadEngine.GlobalNetworkBufferBudgetBytes / connections);

        var desired = mode switch
        {
            BufferMode.LowRam => DownloadEngine.LowRamBufferSize,
            BufferMode.Balanced => DownloadEngine.BalancedBufferSize,
            BufferMode.Fast => DownloadEngine.FastBufferSize,
            _ => connections <= 4
                ? DownloadEngine.AutoDefaultBufferSize
                : connections <= 16
                    ? DownloadEngine.AutoLargeBufferSize
                    : DownloadEngine.FastBufferSize
        };

        if (mode == BufferMode.Auto)
        {
            var autoMinimum = Math.Min(DownloadEngine.AutoMinimumBufferSize, budgetPerConnection);
            return Math.Clamp(
                Math.Min(desired, budgetPerConnection),
                autoMinimum,
                DownloadEngine.FastBufferSize);
        }

        return Math.Max(1, Math.Min(desired, budgetPerConnection));
    }

    public static string ToResourceKey(this BufferMode mode) => mode switch
    {
        BufferMode.LowRam => "BufferLowRam",
        BufferMode.Balanced => "BufferBalanced",
        BufferMode.Fast => "BufferFast",
        _ => "BufferAuto"
    };

    public static bool TryParse(string? value, out BufferMode mode) =>
        Enum.TryParse(value, ignoreCase: true, out mode) && Enum.IsDefined(mode);
}
