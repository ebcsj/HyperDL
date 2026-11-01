using HyperDL.Core;

namespace HyperDL.Tests;

public class UpdateCheckTests
{
    [Theory]
    [InlineData("1.2.0", "v1.3.0", true)]
    [InlineData("1.3.0", "v1.3.0", false)]
    [InlineData("1.3.0", "v1.2.0", false)]
    [InlineData("1.2.0", "1.3.0", true)]
    [InlineData("1.2.9", "v1.3.0", true)]
    [InlineData("1.3.0-beta.1", "v1.3.0", true)]
    [InlineData("1.3.0", "v1.3.0-beta.1", false)]
    [InlineData("1.3.0-beta.1", "v1.3.0-beta.2", true)]
    public void IsNewerVersion_ComparesSemanticVersions(
        string current,
        string latest,
        bool expected)
    {
        Assert.Equal(expected, UpdateCheck.IsNewerVersion(current, latest));
    }

    [Theory]
    [InlineData(null, "v9.9.9")]
    [InlineData("1.2.0", null)]
    [InlineData("1.2.0", "garbage")]
    [InlineData("1.2.0", "v1.2")]
    [InlineData("", "v9.9.9")]
    public void IsNewerVersion_IgnoresUnparseableInput(string? current, string? latest)
    {
        Assert.False(UpdateCheck.IsNewerVersion(current, latest));
    }
}
