using System.Text.Json;
using HyperDL.Platform;

namespace HyperDL.Tests;

public class IpcProtocolTests
{
    [Fact]
    public void ParseRequest_ListCommand()
    {
        var request = IpcProtocol.ParseRequest("""{"cmd":"list"}""");

        Assert.NotNull(request);
        Assert.Equal("list", request!.Command);
        Assert.Empty(request.Urls);
        Assert.Null(request.Connections);
    }

    [Fact]
    public void ParseRequest_AddWithUrlsAndConnections()
    {
        var request = IpcProtocol.ParseRequest(
            """{"cmd":"add","urls":["https://unit.test/a.bin","https://unit.test/b.bin"],"connections":4}""");

        Assert.NotNull(request);
        Assert.Equal("add", request!.Command);
        Assert.Equal(2, request.Urls.Count);
        Assert.Equal(4, request.Connections);
    }

    [Fact]
    public void ParseRequest_InvalidJsonReturnsNull()
    {
        Assert.Null(IpcProtocol.ParseRequest("{not json"));
        Assert.Null(IpcProtocol.ParseRequest(""));
    }

    [Fact]
    public void LooksLikeRequest_DetectsJson()
    {
        Assert.True(IpcProtocol.LooksLikeRequest(""" {"cmd":"list"} """));
        Assert.False(IpcProtocol.LooksLikeRequest("https://unit.test/a.bin"));
    }

    [Fact]
    public void BuildRequest_RoundTripsThroughParseRequest()
    {
        var line = IpcProtocol.BuildRequest("add", ["https://unit.test/a.bin"]);

        var request = IpcProtocol.ParseRequest(line);

        Assert.NotNull(request);
        Assert.Equal("add", request!.Command);
        Assert.Equal(["https://unit.test/a.bin"], request.Urls);
    }

    [Fact]
    public void BuildResponse_EmitsCamelCaseOkAndJobs()
    {
        var json = IpcProtocol.BuildResponse(new IpcResponse(
            true,
            null,
            [new IpcJobSnapshot(
                Guid.Parse("bf58e5a1-1111-4a6b-9a54-000000000001"),
                "a.bin",
                "已完成",
                50,
                500,
                1000,
                @"C:\downloads\a.bin",
                "abc")],
            null));

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(1, root.GetProperty("jobs").GetArrayLength());
        Assert.Equal("a.bin", root.GetProperty("jobs")[0].GetProperty("fileName").GetString());
        Assert.Equal(500, root.GetProperty("jobs")[0].GetProperty("bytesDownloaded").GetInt64());
    }
}
