using System.Linq;
using System.Text.Json;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Разбор диффов OpenCode (<c>GET /api/session/{id}/diff</c>) и подготовка
/// строк патча для карточки: схема <c>file/patch/additions/deletions/status</c>,
/// отбрасывание заголовков файлов, ограничение размера.
/// </summary>
public sealed class OpenCodeDiffTests : IDisposable
{
    readonly List<JsonDocument> _documents = [];

    JsonElement Parse(string json)
    {
        JsonDocument document = JsonDocument.Parse(json);
        _documents.Add(document);
        return document.RootElement;
    }

    public void Dispose()
    {
        foreach (JsonDocument document in _documents) document.Dispose();
    }

    [Fact]
    public void ParseDiffs_ProjectsFileDiffItems()
    {
        JsonElement data = Parse("""
            [
              { "file": "src/App.xaml.cs", "patch": "@@ -1 +1 @@", "additions": 3, "deletions": 1, "status": "modified" },
              { "file": "docs/NEW.md", "patch": "", "additions": 10, "deletions": 0, "status": "added" },
              { "patch": "без файла" }
            ]
            """);

        List<OpenCodeFileDiff> diffs = OpenCodeService.ParseDiffs(data);

        Assert.Collection(diffs,
            diff =>
            {
                Assert.Equal("src/App.xaml.cs", diff.File);
                Assert.Equal("App.xaml.cs", diff.Name);
                Assert.Equal("src", diff.Folder);
                Assert.Equal(3, diff.Additions);
                Assert.Equal(1, diff.Deletions);
                Assert.Equal("+3 \u22121", diff.Stats);
                Assert.Equal("изменён", diff.StatusLabel);
            },
            diff =>
            {
                Assert.Equal("docs/NEW.md", diff.File);
                Assert.Equal("новый файл", diff.StatusLabel);
            });
    }

    [Fact]
    public void ParseDiffs_HandlesNonArray()
    {
        Assert.Empty(OpenCodeService.ParseDiffs(Parse("{}")));
        Assert.Empty(OpenCodeService.ParseDiffs(Parse("null")));
    }

    [Fact]
    public void SplitPatch_ClassifiesLinesAndSkipsFileHeaders()
    {
        const string patch = """
            --- a/src/a.cs
            +++ b/src/a.cs
            @@ -1,3 +1,4 @@
             context
            -removed
            +added
            """;

        List<OpenCodeDiffLine> lines = OpenCodeService.SplitPatch(patch);

        Assert.Equal(new[]
        {
            new OpenCodeDiffLine("hunk", "@@ -1,3 +1,4 @@"),
            new OpenCodeDiffLine("context", " context"),
            new OpenCodeDiffLine("removed", "-removed"),
            new OpenCodeDiffLine("added", "+added"),
        }, lines);
    }

    [Fact]
    public void SplitPatch_KeepsContentThatLooksLikeHeader()
    {
        // Строка содержимого "+++x" не должна считаться заголовком файла.
        List<OpenCodeDiffLine> lines = OpenCodeService.SplitPatch("+++x");

        Assert.Single(lines);
        Assert.Equal("added", lines[0].Kind);
    }

    [Fact]
    public void SplitPatch_LimitsLineCountAndLength()
    {
        string longLine = "+" + new string('a', 400);
        string manyLines = string.Join('\n', Enumerable.Repeat(" line", 320));

        List<OpenCodeDiffLine> longResult = OpenCodeService.SplitPatch(longLine);
        Assert.Equal(241, longResult[0].Text.Length);
        Assert.EndsWith("\u2026", longResult[0].Text);

        List<OpenCodeDiffLine> manyResult = OpenCodeService.SplitPatch(manyLines);
        Assert.Equal("more", manyResult[^1].Kind);
        Assert.Equal(301, manyResult.Count);
    }

    [Fact]
    public void SplitPatch_HandlesEmptyPatch()
    {
        Assert.Empty(OpenCodeService.SplitPatch(null));
        Assert.Empty(OpenCodeService.SplitPatch(""));
    }
}
