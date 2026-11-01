using HyperDL.Localization;

namespace HyperDL.Tests;

public class LocalizationManagerTests
{
    [Fact]
    public void EnglishAndTraditionalChineseTables_HaveIdenticalKeySets()
    {
        var englishKeys = StringTables.English.Keys.ToHashSet(StringComparer.Ordinal);
        var chineseKeys = StringTables.TraditionalChinese.Keys.ToHashSet(StringComparer.Ordinal);

        Assert.Subset(englishKeys, chineseKeys);
        Assert.Subset(chineseKeys, englishKeys);
    }

    [Fact]
    public void Tables_NeverContainBlankValues()
    {
        foreach (var pair in StringTables.English)
        {
            Assert.False(string.IsNullOrWhiteSpace(pair.Value), $"English key '{pair.Key}' is blank.");
        }

        foreach (var pair in StringTables.TraditionalChinese)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(pair.Value),
                $"TraditionalChinese key '{pair.Key}' is blank.");
        }
    }

    [Theory]
    [InlineData("NoCompletedMessage")]
    [InlineData("UrlCopied")]
    [InlineData("UrlUpdatedMessage")]
    [InlineData("OkButton")]
    public void GetString_ResolvesEveryKeyUsedByTheShell(string key)
    {
        Assert.NotEqual(key, LocalizationManager.GetString(key));
    }

    [Fact]
    public void GetString_FallsBackToTheKeyForUnknownEntries()
    {
        Assert.Equal("KeyThatDoesNotExist", LocalizationManager.GetString("KeyThatDoesNotExist"));
    }

    [Fact]
    public void FormatStrings_KeepTheirPlaceholders()
    {
        Assert.Contains("{0}", StringTables.English["BatchAddedFormat"]);
        Assert.Contains("{0}", StringTables.TraditionalChinese["BatchAddedFormat"]);
        Assert.Contains("{0}", StringTables.English["ErrorDetailFormat"]);
        Assert.Contains("{1}", StringTables.TraditionalChinese["ErrorDetailFormat"]);
    }
}
