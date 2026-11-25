using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    [Fact]
    public async Task Check_ApiSuccess_MarksAvailable()
    {
        var handler = new ReleaseStubHandler
        {
            ApiJson = """
                {"tag_name":"v9.9.9","body":"- Новое","assets":[
                  {"name":"OpenIsland.exe","browser_download_url":"https://github.com/mmmatveyz/OpenIsland/releases/download/v9.9.9/OpenIsland.exe","digest":"sha256:abc","size":123}
                ]}
                """,
        };
        Updater updater = NewUpdater(handler);

        await updater.CheckAsync();

        Assert.Equal(Updater.Stage.Available, updater.State);
        Assert.Equal(new Version(9, 9, 9), updater.LatestVersion);
        Assert.False(updater.RateLimited);
        Assert.Single(updater.Notes);
        Assert.Equal(1, handler.ApiCalls);
        Assert.Equal(0, handler.PageCalls);
    }

    [Fact]
    public async Task Check_ApiRateLimited_FallsBackToReleasePage()
    {
        var handler = new ReleaseStubHandler
        {
            ApiStatus = HttpStatusCode.Forbidden,
            RedirectLocation = "https://github.com/mmmatveyz/OpenIsland/releases/tag/v9.9.9",
        };
        Updater updater = NewUpdater(handler);

        await updater.CheckAsync();

        Assert.True(updater.RateLimited);
        Assert.Equal(Updater.Stage.Available, updater.State);
        Assert.Equal(new Version(9, 9, 9), updater.LatestVersion);
        Assert.Equal(1, handler.ApiCalls);
        Assert.Equal(1, handler.PageCalls);
    }

    [Fact]
    public async Task Check_ApiOffline_FallsBackToReleasePage_WithoutRateLimitFlag()
    {
        var handler = new ReleaseStubHandler
        {
            ApiStatus = HttpStatusCode.InternalServerError,
            RedirectLocation = "https://github.com/mmmatveyz/OpenIsland/releases/tag/v9.9.9",
        };
        Updater updater = NewUpdater(handler);

        await updater.CheckAsync();

        Assert.False(updater.RateLimited);
        Assert.Equal(Updater.Stage.Available, updater.State);
    }

    [Fact]
    public async Task Check_BothChannelsUnavailable_ReportsFailed()
    {
        var handler = new ReleaseStubHandler { ApiStatus = HttpStatusCode.Forbidden };
        Updater updater = NewUpdater(handler);

        await updater.CheckAsync();

        Assert.Equal(Updater.Stage.Failed, updater.State);
        Assert.True(updater.RateLimited);
    }

    [Fact]
    public async Task Check_FailureIsThrottled_ButForceRetries()
    {
        var handler = new ReleaseStubHandler { ApiStatus = HttpStatusCode.Forbidden };
        Updater updater = NewUpdater(handler);

        await updater.CheckAsync();
        Assert.Equal(Updater.Stage.Failed, updater.State);
        Assert.Equal(1, handler.ApiCalls);
        Assert.Equal(1, handler.PageCalls);

        // В пределах паузы после неудачи повторные неявные проверки не ходят в сеть.
        await updater.CheckAsync();
        Assert.Equal(1, handler.ApiCalls);
        Assert.Equal(1, handler.PageCalls);

        // Явное «Попробовать ещё раз» (force) паузу обходит.
        await updater.CheckAsync(force: true);
        Assert.Equal(2, handler.ApiCalls);
        Assert.Equal(2, handler.PageCalls);
    }

    [Theory]
    [InlineData("https://github.com/mmmatveyz/OpenIsland/releases/tag/v1.2.3", "v1.2.3")]
    [InlineData("https://github.com/mmmatveyz/OpenIsland/releases/tag/v1.2.3?ref=x", "v1.2.3")]
    [InlineData("https://github.com/mmmatveyz/OpenIsland/releases", null)]
    [InlineData("", null)]
    public void ExtractTag_ReadsTagFromReleaseUrl(string url, string? expected)
    {
        Assert.Equal(expected, Updater.ExtractTag(url));
    }

    [Fact]
    public void AssetUrl_PointsToRepositoryAsset()
    {
        Assert.Equal(
            "https://github.com/mmmatveyz/OpenIsland/releases/download/v1.2.3/OpenIsland.exe",
            Updater.AssetUrl("v1.2.3"));
    }

    static Updater NewUpdater(ReleaseStubHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        return new Updater(client, client);
    }
}

/// <summary>
/// Подменный транспорт апдейтера: по-разному отвечает на GitHub API и на страницу релиза.
/// </summary>
sealed class ReleaseStubHandler : HttpMessageHandler
{
    public HttpStatusCode ApiStatus { get; init; } = HttpStatusCode.OK;
    public string ApiJson { get; init; } = "{}";
    public string? RedirectLocation { get; init; }
    public int ApiCalls { get; private set; }
    public int PageCalls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri?.AbsoluteUri ?? "";
        if (url.Contains("api.github.com", StringComparison.Ordinal))
        {
            ApiCalls++;
            return Task.FromResult(new HttpResponseMessage(ApiStatus)
            {
                Content = new StringContent(ApiJson, Encoding.UTF8, "application/json"),
            });
        }

        PageCalls++;
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        if (RedirectLocation != null) response.Headers.Location = new Uri(RedirectLocation);
        return Task.FromResult(response);
    }
}
