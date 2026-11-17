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
    public void ParseMessages_ShowsToolOnlyTurnsAsActivityLines()
    {
        JsonElement data = Parse("""
            [
              { "info": { "role": "user" }, "parts": [{ "type": "text", "text": "Почини баг" }] },
              { "info": { "role": "assistant" }, "parts": [
                { "type": "reasoning", "text": "скрытые рассуждения" },
                { "type": "text", "text": "Готово" },
                { "type": "tool", "name": "edit" }
              ] },
              { "info": { "role": "assistant" }, "parts": [
                { "type": "reasoning", "text": "скрытые рассуждения" },
                { "type": "tool", "name": "read", "state": { "input": { "filePath": "C:\\work\\MainWindow.xaml" } } }
              ] },
              { "info": { "role": "system" }, "parts": [{ "type": "text", "text": "не показывать" }] }
            ]
            """);

        List<OpenCodeMessage> messages = OpenCodeService.ParseMessages(data);

        Assert.Collection(messages,
            message => Assert.Equal(new OpenCodeMessage("user", "Почини баг"), message),
            message => Assert.Equal(new OpenCodeMessage("assistant", "Готово"), message),
            message =>
            {
                Assert.Equal("tool", message.Role);
                Assert.True(message.IsActivity);
                Assert.Equal("⚙ read: C:\\work\\MainWindow.xaml", message.Text);
                Assert.Equal("", message.Label);
            });
    }

    [Fact]
    public void ParseMessages_JoinsSeveralToolCallsIntoOneActivityLine()
    {
        JsonElement data = Parse("""
            [
              { "type": "assistant", "content": [
                { "type": "reasoning", "text": "план" },
                { "type": "tool", "name": "shell", "state": { "status": "completed", "input": { "command": "dotnet build" } } },
                { "type": "tool", "name": "grep", "state": { "input": { "pattern": "PillShapes" } } }
              ] },
              { "type": "assistant", "content": [ { "type": "reasoning", "text": "только рассуждения" } ] }
            ]
            """);

        OpenCodeMessage message = Assert.Single(OpenCodeService.ParseMessages(data));

        Assert.Equal("⚙ shell: dotnet build · grep: PillShapes", message.Text);
    }

    [Fact]
    public void ParseMessages_SupportsLegacyContentAndLimitsLargeText()
    {
        string longText = new('я', 12020);
        string json = "[{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"Старый формат\"}]},"
            + "{\"role\":\"user\",\"content\":[{\"type\":\"image\"},{\"type\":\"text\",\"text\":\"" + longText + "\"}]}]";
        JsonElement data = Parse(json);

        List<OpenCodeMessage> messages = OpenCodeService.ParseMessages(data);

        Assert.Equal(2, messages.Count);
        Assert.Equal("Старый формат", messages[0].Text);
        Assert.Equal(12000, messages[1].Text.Length);
    }

    [Fact]
    public void ParseMessages_HandlesNonArray()
    {
        Assert.Empty(OpenCodeService.ParseMessages(Parse("{}")));
    }

    [Fact]
    public void ParseMessages_ReadsOpenCodeV2TypeAndContent()
    {
        // Реальный ответ V2: роль в "type", текст — в "content"; помимо сообщений
        // приходят непроекционные события (idle, compaction, model-switched).
        JsonElement data = Parse("""
            [
              { "type": "idle", "content": [] },
              { "type": "model-switched", "agent": "build", "content": [] },
              { "type": "user", "content": [
                { "type": "text", "text": "Почини баг" }
              ] },
              { "type": "assistant", "content": [
                { "type": "reasoning", "text": "скрытые рассуждения" },
                { "type": "text", "text": "Готово" },
                { "type": "tool", "name": "edit" }
              ] },
              { "type": "compaction", "content": [] }
            ]
            """);

        List<OpenCodeMessage> messages = OpenCodeService.ParseMessages(data);

        Assert.Collection(messages,
            message => Assert.Equal(new OpenCodeMessage("user", "Почини баг"), message),
            message => Assert.Equal(new OpenCodeMessage("assistant", "Готово"), message));
    }

    [Fact]
    public void ParseMessages_ReadsV2UserTextAndIgnoresAttachments()
    {
        // Сообщение пользователя в V2 не имеет "parts"/"content": текст — в "text",
        // он же дублируется в "metadata.displayText"; вложение — base64 в "files".
        JsonElement data = Parse("""
            [
              { "type": "user",
                "text": "Было бы хорошо убирать лишние теги",
                "metadata": { "displayText": "Было бы хорошо убирать лишние теги" },
                "files": [ { "name": "shot.png", "data": "AAAABBBBCCCC" } ] },
              { "type": "user", "files": [ { "name": "shot.png", "data": "AAAABBBB" } ] }
            ]
            """);

        List<OpenCodeMessage> messages = OpenCodeService.ParseMessages(data);

        Assert.Collection(messages,
            message => Assert.Equal(new OpenCodeMessage("user", "Было бы хорошо убирать лишние теги"), message),
            message => Assert.Equal(new OpenCodeMessage("user", "[вложение]"), message));
    }

    [Fact]
    public void MergeMessages_PreservesLocalUserAndAssistantWhenServerHistoryIsStale()
    {
        OpenCodeMessage[] staleHistory = [new("user", "старый запрос")];
        OpenCodeMessage[] current = [
            new("user", "новый запрос"),
            new("assistant", "ответ, полученный через SSE")
        ];

        List<OpenCodeMessage> merged = OpenCodeService.MergeMessages(staleHistory, current);

        Assert.Equal(new[]
        {
            new OpenCodeMessage("user", "старый запрос"),
            new OpenCodeMessage("user", "новый запрос"),
            new OpenCodeMessage("assistant", "ответ, полученный через SSE")
        }, merged);
    }

    [Fact]
    public void MergeMessages_KeepsOlderPagesAndLiveTailAroundFreshPage()
    {
        OpenCodeMessage older = new("user", "старое", "m1");
        OpenCodeMessage pageFirst = new("user", "свежее", "m2");
        OpenCodeMessage pageLast = new("assistant", "ответ", "m3");
        OpenCodeMessage live = new("assistant", "живой ответ");

        // Свежая страница пересекается с хвостом показанного: старый префикс сохраняется.
        Assert.Equal(
            new[] { older, pageFirst, pageLast },
            OpenCodeService.MergeMessages([pageFirst, pageLast], [older, pageFirst]));

        // Ответ, пришедший по SSE во время запроса, не теряется.
        Assert.Equal(
            new[] { older, pageLast, live },
            OpenCodeService.MergeMessages([pageLast], [older, pageLast, live]));
    }

    [Fact]
    public void MergeOlder_PrependsPageAndDropsBoundaryDuplicates()
    {
        OpenCodeMessage first = new("user", "первое", "m1");
        OpenCodeMessage second = new("assistant", "второе", "m2");

        Assert.Equal(
            new[] { first, second },
            OpenCodeService.MergeOlder([first, second], [second]));
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
