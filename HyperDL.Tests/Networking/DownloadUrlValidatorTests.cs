using HyperDL.Core;

namespace HyperDL.Tests;

public class DownloadUrlValidatorTests
{
    [Theory]
    [InlineData("http://example.com/file.zip")]
    [InlineData("https://example.com/file.zip")]
    public void IsSupported_AllowsHttpAndHttps(string value)
    {
        Assert.True(DownloadUrlValidator.IsSupported(new Uri(value)));
    }

    [Theory]
    [InlineData("file:///C:/file.zip")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/plain,hello")]
    [InlineData("shell:open")]
    [InlineData("powershell:command")]
    [InlineData("cmd:command")]
    [InlineData("ftp://example.com/file.zip")]
    public void IsSupported_RejectsNonHttpSchemes(string value)
    {
        Assert.False(DownloadUrlValidator.IsSupported(new Uri(value)));
    }

    [Fact]
    public void IsSupported_RejectsNullUri()
    {
        Assert.False(DownloadUrlValidator.IsSupported(null));
    }
}
