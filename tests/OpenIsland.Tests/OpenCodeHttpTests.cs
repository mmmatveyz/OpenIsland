using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Поведение HTTP-клиента OpenCode на подменном транспорте: успешный опрос
/// состояния, разбор ошибок демона (авторизация, сбой, офлайн) и отсутствие
/// исключений наружу. Живой демон и сеть не используются.
/// </summary>
public class OpenCodeHttpTests
{
    const string BaseUrl = "http://stub.invalid";

    static OpenCodeService Service(Func<HttpRequestMessage, HttpResponseMessage> reply) =>
        new(new StubHttpHandler(reply), BaseUrl);

    [Fact]
    public async Task Refresh_PopulatesStateFromStubServer()
    {
        using OpenCodeService service = Service(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/session/active" => StubHttpHandler.Json("""{"data":{"ses_1":{"type":"running"}}}"""),
            "/api/session" => StubHttpHandler.Json(
                """{"data":[{"id":"ses_1","title":"Починить баг","location":{"directory":"C:\\work"},"model":{"id":"m1","providerID":"p1"}}]}"""),
            "/api/model" => StubHttpHandler.Json("""{"data":[{"id":"m1","name":"Model One","providerID":"p1"}]}"""),
            _ => StubHttpHandler.Json("""{"data":{}}"""),
        });

        await service.RefreshAsync();

        Assert.True(service.IsConnected);
        Assert.True(service.IsBusy);
        Assert.Equal("Починить баг", service.SessionTitle);
        Assert.Equal("Model One", service.ModelName);
        Assert.Single(service.Sessions);
        Assert.Single(service.Models);
    }

    [Fact]
    public async Task Refresh_OnUnauthorized_ReportsAuthRequired()
    {
        using OpenCodeService service = Service(
            _ => StubHttpHandler.Json("""{"error":"nope"}""", HttpStatusCode.Unauthorized));

        await service.RefreshAsync();

        Assert.False(service.IsConnected);
        Assert.Contains("авторизац", service.StatusText);
    }

    [Fact]
    public async Task Refresh_OnServerError_ReportsOffline()
    {
        using OpenCodeService service = Service(
            _ => StubHttpHandler.Json("boom", HttpStatusCode.InternalServerError));

        await service.RefreshAsync();

        Assert.False(service.IsConnected);
        Assert.Equal("Ошибка OpenCode", service.StatusText);
    }

    [Fact]
    public async Task SendAsync_MapsTransportFailuresToOfflineResult()
    {
        using OpenCodeService timedOut = Service(_ => throw new TaskCanceledException());
        using OpenCodeService broken = Service(_ => throw new HttpRequestException("нет связи"));

        (int timeoutStatus, string timeoutBody) = await timedOut.SendAsync(HttpMethod.Get, "/api/x", null, allowRepair: false);
        (int brokenStatus, string brokenBody) = await broken.SendAsync(HttpMethod.Get, "/api/x", null, allowRepair: false);

        Assert.Equal(0, timeoutStatus);
        Assert.Equal("timeout", timeoutBody);
        Assert.Equal(0, brokenStatus);
        Assert.Equal("нет связи", brokenBody);
    }

    [Fact]
    public void DescribeFailure_MapsKnownStatusCodes()
    {
        Assert.Contains("не отвечает", OpenCodeService.DescribeFailure("Задача", 0, "timeout"));
        Assert.Contains("нет связи", OpenCodeService.DescribeFailure("Задача", 0, "connection refused"));
        Assert.Contains("авторизац", OpenCodeService.DescribeFailure("Задача", 401, ""));
        Assert.Contains("не найдена", OpenCodeService.DescribeFailure("Задача", 404, ""));
        Assert.Contains("занята", OpenCodeService.DescribeFailure("Задача", 409, ""));
        Assert.Contains("код 500", OpenCodeService.DescribeFailure("Задача", 500, ""));
    }
}
