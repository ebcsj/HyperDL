using System.IO;
using System.Text;
using HyperDL.Platform;

namespace HyperDL.Tests;

public class NativeMessagingFrameTests
{
    [Fact]
    public async Task EncodeDecode_RoundTripsPayload()
    {
        var payload = """{"type":"add","url":"https://unit.test/a.bin"}""";
        using var stream = new MemoryStream(NativeMessagingFrame.Encode(payload));

        var decoded = await NativeMessagingFrame.DecodeAsync(stream);

        Assert.Equal(payload, decoded);
    }

    [Fact]
    public async Task DecodeAsync_ReadsSequentialFrames()
    {
        using var stream = new MemoryStream();
        stream.Write(NativeMessagingFrame.Encode("""{"n":1}"""));
        stream.Write(NativeMessagingFrame.Encode("""{"n":2}"""));
        stream.Position = 0;

        var first = await NativeMessagingFrame.DecodeAsync(stream);
        var second = await NativeMessagingFrame.DecodeAsync(stream);

        Assert.Equal("""{"n":1}""", first);
        Assert.Equal("""{"n":2}""", second);
    }

    [Fact]
    public async Task DecodeAsync_EmptyStreamReturnsNull()
    {
        using var stream = new MemoryStream();

        Assert.Null(await NativeMessagingFrame.DecodeAsync(stream));
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedFrames()
    {
        using var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(2 * 1024 * 1024));
        stream.Position = 0;

        Assert.Null(await NativeMessagingFrame.DecodeAsync(stream));
    }

    [Fact]
    public void Encode_PrefixesLittleEndianLength()
    {
        var frame = NativeMessagingFrame.Encode("{}");

        Assert.Equal(4 + 2, frame.Length);
        Assert.Equal(2, BitConverter.ToInt32(frame, 0));
        Assert.Equal(Encoding.UTF8.GetBytes("{}"), frame[4..]);
    }
}
