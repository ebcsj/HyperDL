using System.IO;
using HyperDL.Core;

namespace HyperDL.Tests;

public class FileNameSanitizerTests
{
    [Theory]
    [InlineData("movie.mp4", "movie.mp4")]
    [InlineData("../movie.mp4", ".._movie.mp4")]
    [InlineData("..\\movie.mp4", ".._movie.mp4")]
    [InlineData("folder/movie.mp4", "folder_movie.mp4")]
    [InlineData("folder\\movie.mp4", "folder_movie.mp4")]
    public void Sanitize_ReturnsOnlySafeFilename(string input, string expected)
    {
        var result = FileNameSanitizer.Sanitize(input);

        Assert.Equal(expected, result);
        Assert.Equal(Path.GetFileName(result), result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("\\", result);
        Assert.DoesNotContain("../", result);
        Assert.DoesNotContain("..\\", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    public void Sanitize_UsesFallbackForBlankOrInvalidNames(string input)
    {
        var result = FileNameSanitizer.Sanitize(input);

        Assert.Equal("download", result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Sanitize_ReplacesWindowsInvalidFilenameCharacters()
    {
        var result = FileNameSanitizer.Sanitize("<movie>:\"|?*.mp4");

        Assert.DoesNotContain("<", result);
        Assert.DoesNotContain(">", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("\"", result);
        Assert.DoesNotContain("|", result);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain("*", result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Sanitize_TruncatesVeryLongFilenameSafely()
    {
        var result = FileNameSanitizer.Sanitize($"{new string('a', 300)}.mp4");

        Assert.True(result.Length <= 180);
        Assert.EndsWith(".mp4", result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Sanitize_PreservesNameWithoutExtension()
    {
        Assert.Equal("archive", FileNameSanitizer.Sanitize("archive"));
    }
}
