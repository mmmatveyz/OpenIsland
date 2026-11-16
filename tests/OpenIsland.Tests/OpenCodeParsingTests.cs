using System.Text.Json;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Разбор ответов демона OpenCode. Документы держатся живыми до конца теста,
/// потому что <see cref="JsonElement"/> — это представление внутрь них.
/// </summary>
public sealed class OpenCodeParsingTests : IDisposable
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
    public void GetString_ReturnsEmptyForMissingOrNonStringValues()
    {
        JsonElement element = Parse("""{"title":"Задача","count":5}""");

        Assert.Equal("Задача", OpenCodeService.GetString(element, "title"));
        Assert.Equal("", OpenCodeService.GetString(element, "count"));
        Assert.Equal("", OpenCodeService.GetString(element, "missing"));
        Assert.Equal("", OpenCodeService.GetString(Parse("[]"), "title"));
    }

    [Fact]
    public void ParseRunningSessions_TakesOnlyRunningObjects()
    {
        JsonElement active = Parse("""
            {
              "ses_a": { "type": "running" },
              "ses_b": { "type": "idle" },
              "ses_c": { "type": "running", "extra": 1 },
              "ses_d": "running"
            }
            """);

        HashSet<string> running = OpenCodeService.ParseRunningSessions(active);

        Assert.Equal(new[] { "ses_a", "ses_c" }, running.OrderBy(name => name).ToArray());
    }

    [Fact]
    public void ParseRunningSessions_HandlesNonObject()
    {
        Assert.Empty(OpenCodeService.ParseRunningSessions(Parse("[]")));
    }

    [Fact]
    public void ParseSessions_ProjectsFieldsAndFlagsRunningSessions()
    {
        JsonElement data = Parse("""
            [
              {
                "id": "ses_1",
                "title": "Починить баг",
                "location": { "directory": "C:\\work" },
                "model": { "id": "claude-sonnet", "providerID": "anthropic" },
                "time": { "updated": 1700000000000 }
              },
              { "id": "ses_2" },
              { "title": "Без идентификатора" }
            ]
            """);

        List<OpenCodeSessionInfo> sessions = OpenCodeService.ParseSessions(data, ["ses_1"]);

        Assert.Equal(2, sessions.Count); // запись без id отброшена

        OpenCodeSessionInfo first = sessions[0];
        Assert.Equal("ses_1", first.Id);
        Assert.Equal("Починить баг", first.Title);
        Assert.Equal("C:\\work", first.Directory);
        Assert.Equal("claude-sonnet", first.ModelId);
        Assert.Equal("anthropic", first.ModelProviderId);
        Assert.True(first.IsRunning);
        Assert.Equal(1700000000000, first.UpdatedAt);

        OpenCodeSessionInfo second = sessions[1];
        Assert.Equal("ses_2", second.Id);
        Assert.Equal("Без названия", second.Title);
        Assert.Equal("", second.Directory);
        Assert.Equal("", second.ModelId);
        Assert.Equal("", second.ModelProviderId);
        Assert.False(second.IsRunning);
        Assert.Equal(0, second.UpdatedAt);
    }

    [Fact]
    public void ParseSessions_HandlesNonArray()
    {
        Assert.Empty(OpenCodeService.ParseSessions(Parse("{}"), []));
    }

    [Fact]
    public void ParseModels_SkipsDisabledAndFallsBackToIdForName()
    {
        JsonElement data = Parse("""
            [
              { "id": "gpt-5", "name": "GPT-5", "providerID": "openai" },
              { "id": "no-name", "providerID": "x" },
              { "id": "off", "name": "Off", "enabled": false },
              { "name": "без id" }
            ]
            """);

        List<OpenCodeModelInfo> models = OpenCodeService.ParseModels(data);

        Assert.Equal(2, models.Count);
        Assert.Equal(new OpenCodeModelInfo("gpt-5", "GPT-5", "openai"), models[0]);
        Assert.Equal("no-name", models[1].Id);
        Assert.Equal("no-name", models[1].Name);
    }

    [Fact]
    public void ParseModels_HandlesNonArray()
    {
        Assert.Empty(OpenCodeService.ParseModels(Parse("null")));
    }

    [Fact]
    public void DescribeModel_PrefersDisplayNameAndFallsBackToId()
    {
        OpenCodeModelInfo[] models = [new("gpt-5", "GPT-5", "openai")];

        Assert.Equal("GPT-5", OpenCodeService.DescribeModel("gpt-5", "openai", models));
        Assert.Equal("gpt-5", OpenCodeService.DescribeModel("gpt-5", "other-provider", models));
        Assert.Equal("Модель не выбрана", OpenCodeService.DescribeModel("", "", models));
    }

    [Fact]
    public void Shorten_TruncatesWithEllipsis()
    {
        Assert.Equal("abc", OpenCodeService.Shorten("abc", 5));
        Assert.Equal("ab…", OpenCodeService.Shorten("abcdef", 3));
    }
}
