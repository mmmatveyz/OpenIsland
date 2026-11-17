using System.Threading.Tasks;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Политика повторных попыток модели OpenCode: событие <c>session.status</c>
/// со статусом <c>retry</c> держит агента занятым и объясняет причину, <c>idle</c>
/// её снимает, а события чужой сессии состояние интерфейса не меняют.
/// </summary>
public class OpenCodeRetryTests
{
    const string BaseUrl = "http://stub.invalid";

    static async Task<OpenCodeService> ConnectedAsync()
    {
        OpenCodeService service = new(new StubHttpHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/session/active" => StubHttpHandler.Json("""{"data":{}}"""),
            "/api/session" => StubHttpHandler.Json(
                """{"data":[{"id":"ses_1","title":"Починить баг","location":{"directory":"C:\\work"},"model":{"id":"m1","providerID":"p1"}}]}"""),
            "/api/model" => StubHttpHandler.Json("""{"data":[{"id":"m1","name":"Model One","providerID":"p1"}]}"""),
            "/api/session/ses_1/message" => StubHttpHandler.Json("""{"data":[],"cursor":{"next":""}}"""),
            _ => StubHttpHandler.Json("""{"data":{}}"""),
        }), BaseUrl);

        await service.RefreshAsync();
        return service;
    }

    static string Status(string sessionId, string type) =>
        "{\"type\":\"session.status\",\"data\":{\"sessionID\":\"" + sessionId
        + "\",\"status\":{\"type\":\"" + type + "\"}}}";

    [Fact]
    public async Task RetryStatus_KeepsAgentBusyAndExplainsRetry()
    {
        using OpenCodeService service = await ConnectedAsync();
        Assert.False(service.IsBusy);

        service.HandleEvent(Status("ses_1", "retry"));

        Assert.True(service.IsBusy);
        Assert.Equal("Повтор запроса к модели...", service.LastError);
        Assert.Contains("Починить баг", service.StatusText);
    }

    [Fact]
    public async Task RetryThenIdle_ClearsBusyAndError()
    {
        using OpenCodeService service = await ConnectedAsync();
        service.HandleEvent(Status("ses_1", "retry"));

        service.HandleEvent(Status("ses_1", "idle"));

        Assert.False(service.IsBusy);
        Assert.Equal("", service.LastError);
        Assert.Equal("OpenCode готов", service.StatusText);
    }

    [Fact]
    public async Task RetryStatus_ForAnotherSession_IsIgnored()
    {
        using OpenCodeService service = await ConnectedAsync();

        service.HandleEvent(Status("ses_2", "retry"));

        Assert.False(service.IsBusy);
        Assert.Equal("", service.LastError);
        Assert.Equal("OpenCode готов", service.StatusText);
    }
}
