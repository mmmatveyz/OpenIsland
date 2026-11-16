using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenIsland.Tests;

/// <summary>
/// Подменный HTTP-транспорт: вместо сети отдаёт заранее заданный ответ.
/// Позволяет проверять обработку статусов и сбоев без живого демона OpenCode.
/// </summary>
sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
{
    /// <summary>Ответ с JSON-телом и заданным статусом (по умолчанию 200).</summary>
    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Транспорт, который на любой запрос выбрасывает заданное исключение.</summary>
    public static StubHttpHandler Throwing(Exception error) => new(_ => throw error);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(reply(request));
}
