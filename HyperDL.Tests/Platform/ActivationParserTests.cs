using HyperDL.Platform;

namespace HyperDL.Tests;

public class ActivationParserTests
{
    [Fact]
    public void Parse_PlainUrl()
    {
        var request = ActivationParser.Parse("https://unit.test/a.bin");

        Assert.Single(request.Uris);
        Assert.Equal("https://unit.test/a.bin", request.Uris[0].ToString());
        Assert.False(request.External);
        Assert.Null(request.Connections);
        Assert.Null(request.Confirm);
        Assert.Null(request.InvalidText);
    }

    [Fact]
    public void Parse_HyperdlProtocolCarriesOverrides()
    {
        var request = ActivationParser.Parse(
            "hyperdl://add?url=https%3A%2F%2Funit.test%2Fa.bin&connections=8&confirm=0");

        Assert.Single(request.Uris);
        Assert.Equal("https://unit.test/a.bin", request.Uris[0].ToString());
        Assert.True(request.External);
        Assert.Equal(8, request.Connections);
        Assert.Equal(false, request.Confirm);
        Assert.Null(request.InvalidText);
    }

    [Fact]
    public void Parse_HyperdlConfirmOne()
    {
        var request = ActivationParser.Parse(
            "hyperdl://add?url=https%3A%2F%2Funit.test%2Fa.bin&connections=auto&confirm=1");

        Assert.Single(request.Uris);
        Assert.True(request.External);
        Assert.Null(request.Connections);
        Assert.Equal(true, request.Confirm);
    }

    [Fact]
    public void Parse_InvalidConnectionsValueIsIgnored()
    {
        var request = ActivationParser.Parse(
            "hyperdl://add?url=https%3A%2F%2Funit.test%2Fa.bin&connections=7");

        Assert.Single(request.Uris);
        Assert.Null(request.Connections);
    }

    [Fact]
    public void Parse_AddFlag()
    {
        var request = ActivationParser.Parse("--add https://unit.test/a.bin");

        Assert.Single(request.Uris);
        Assert.False(request.External);
    }

    [Fact]
    public void Parse_SilentlyIgnoresNonHttpSchemes()
    {
        // Non-HTTP tokens are neither added nor surfaced as invalid: only
        // http(s) lookalikes land in InvalidText.
        var request = ActivationParser.Parse("ftp://unit.test/a.bin file:///C:/x");

        Assert.Empty(request.Uris);
        Assert.Null(request.InvalidText);
    }

    [Fact]
    public void Parse_DeduplicatesUrls()
    {
        var request = ActivationParser.Parse(
            "https://unit.test/a.bin https://unit.test/a.bin");

        Assert.Single(request.Uris);
    }

    [Fact]
    public void Parse_EmptyPayload()
    {
        var request = ActivationParser.Parse("");

        Assert.Empty(request.Uris);
        Assert.False(request.External);
        Assert.Null(request.Connections);
        Assert.Null(request.Confirm);
    }
}
