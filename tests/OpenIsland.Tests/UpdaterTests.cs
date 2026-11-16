using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("1.0.1.5", 1, 0, 1)]
    [InlineData("0.0.0", 0, 0, 0)]
    public void Normalize_KeepsThreeVersionComponents(string text, int major, int minor, int build)
    {
        Version normalized = Updater.Normalize(Version.Parse(text));

        Assert.Equal(new Version(major, minor, build), normalized);
    }

    [Fact]
    public void Normalize_DefaultsMissingBuildToZero()
    {
        Assert.Equal(new Version(2, 5, 0), Updater.Normalize(new Version(2, 5)));
    }

    [Fact]
    public void NormalizedVersions_CompareByNumericParts()
    {
        Version older = Updater.Normalize(Version.Parse("1.0.0"));
        Version newer = Updater.Normalize(Version.Parse("1.0.1"));
        Version same = Updater.Normalize(Version.Parse("1.0.1"));

        Assert.True(newer > older);
        Assert.False(newer > same);
    }

    [Fact]
    public void ParseNotes_ExtractsBulletTitlesAndStripsMarkup()
    {
        const string body = """
            ## Что нового
            - Первое изменение
            * Второе изменение
            • Третье изменение
              - Вложенный пункт
            - **Жирный** и `код`
            просто текст без маркера
            - https://github.com/mmmatveyz/OpenIsland
            """;

        string[] notes = Updater.ParseNotes(body);

        Assert.Equal(
            ["Первое изменение", "Второе изменение", "Третье изменение", "Вложенный пункт", "Жирный и код"],
            notes);
    }

    [Fact]
    public void ParseNotes_CapsAtSixEntries()
    {
        string body = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"- Пункт {i}"));

        Assert.Equal(6, Updater.ParseNotes(body).Length);
    }

    [Fact]
    public void ParseNotes_ReturnsNothingForEmptyOrPlainText()
    {
        Assert.Empty(Updater.ParseNotes(""));
        Assert.Empty(Updater.ParseNotes("просто текст без списка"));
    }
}
