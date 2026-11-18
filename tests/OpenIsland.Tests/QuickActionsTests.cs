using System.IO;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Логика карточки «Быстрый запуск»: хранение своих программ строкой реестра,
/// фильтр поддерживаемых файлов и параметры запуска через оболочку Windows.
/// </summary>
public class QuickActionsTests
{
    [Fact]
    public void Parse_ReadsNameTargetAndArguments()
    {
        IReadOnlyList<QuickAction> actions = QuickActions.Parse(
            "Проводник|C:\\Windows\\explorer.exe\nРедактор|C:\\Tools\\edit.exe|--safe --profile work");

        Assert.Equal(2, actions.Count);
        Assert.Equal(new QuickAction("Проводник", @"C:\Windows\explorer.exe"), actions[0]);
        Assert.Equal(new QuickAction("Редактор", @"C:\Tools\edit.exe", "--safe --profile work"), actions[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("без разделителя")]
    [InlineData("Имя|   ")]
    [InlineData("Имя|")]
    [InlineData("\n\n\r\n")]
    public void Parse_SkipsEmptyAndMalformedLines(string text) =>
        Assert.Empty(QuickActions.Parse(text));

    [Fact]
    public void Parse_AcceptsArgumentsWithPipes()
    {
        QuickAction action = Assert.Single(QuickActions.Parse("Имя|tool.exe|--a|--b"));
        Assert.Equal("--a|--b", action.Arguments);
    }

    [Fact]
    public void Format_RoundTripsOwnActions()
    {
        QuickAction[] actions =
        [
            new("Проводник", @"C:\Windows\explorer.exe"),
            new("Редактор", @"C:\Program Files\Tool\tool.exe", "--safe"),
        ];

        Assert.Equal(actions, QuickActions.Parse(QuickActions.Format(actions)));
    }

    [Fact]
    public void Format_StripsPipeAndNewlinesFromNamesButKeepsThemInArguments()
    {
        string text = QuickActions.Format([new QuickAction("A|B\r\nC", "tool.exe", "--a|--b")]);

        Assert.Equal("A B  C|tool.exe|--a|--b", text);
        Assert.Equal("--a|--b", Assert.Single(QuickActions.Parse(text)).Arguments);
    }

    [Theory]
    [InlineData(".exe", true)]
    [InlineData(".EXE", true)]
    [InlineData(".lnk", true)]
    [InlineData(".bat", true)]
    [InlineData(".cmd", true)]
    [InlineData(".txt", false)]
    [InlineData(".ps1", false)]
    [InlineData("", false)]
    public void IsSupported_AcceptsOnlyShellExecutables(string extension, bool expected) =>
        Assert.Equal(expected, QuickActions.IsSupported("C:\\Tools\\app" + extension));

    [Fact]
    public void CanStart_LetsShellResolveBuiltinsAndUris()
    {
        Assert.True(QuickActions.CanStart(new QuickAction("Проводник", "explorer.exe")));
        Assert.True(QuickActions.CanStart(new QuickAction("Параметры", "ms-settings:")));
        Assert.True(QuickActions.CanStart(new QuickAction("Ножницы", "ms-screenclip:")));
        Assert.False(QuickActions.CanStart(new QuickAction("Пусто", "")));
    }

    [Fact]
    public void CanStart_RequiresAnExistingFileForFullPaths()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".exe");
        Assert.False(QuickActions.CanStart(new QuickAction("Нет", missing)));

        string present = Path.GetTempFileName();
        try
        {
            Assert.True(QuickActions.CanStart(new QuickAction("Есть", present)));
        }
        finally
        {
            File.Delete(present);
        }
    }

    [Fact]
    public void StartInfo_LaunchesThroughShellInTheFileFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "OpenIslandTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "tool.exe");
        File.WriteAllText(file, "");
        try
        {
            var info = QuickActions.StartInfo(new QuickAction("Tool", file, "--flag"));

            Assert.True(info.UseShellExecute);
            Assert.Equal(file, info.FileName);
            Assert.Equal("--flag", info.Arguments);
            Assert.Equal(folder, info.WorkingDirectory);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FromFile_UsesFileNameWithoutExtensionAndFullPath()
    {
        string full = Path.GetFullPath(Path.Combine("tools", "My Tool.exe"));
        QuickAction action = QuickActions.FromFile("tools\\My Tool.exe");

        Assert.Equal("My Tool", action.Name);
        Assert.Equal(full, action.Target);
        Assert.Equal(full, action.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToTargetName()
    {
        Assert.Equal("Проводник", new QuickAction("Проводник", "explorer.exe").DisplayName);
        Assert.Equal("tool", new QuickAction("", @"C:\Tools\tool.exe").DisplayName);
    }

    [Fact]
    public void SystemActions_HaveTitlesTargetsAndAreLaunchable()
    {
        IReadOnlyList<QuickAction> actions = QuickActions.SystemActions();

        Assert.Equal(8, actions.Count);
        Assert.All(actions, action =>
        {
            Assert.NotEmpty(action.Name);
            Assert.NotEmpty(action.Target);
            Assert.True(QuickActions.CanStart(action));
        });
        Assert.Equal(actions.Count, actions.Select(action => action.Name).Distinct().Count());
    }
}
