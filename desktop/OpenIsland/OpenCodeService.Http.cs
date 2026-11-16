using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace OpenIsland;

public sealed partial class OpenCodeService
{
    // ---------------------------------------------------------------- HTTP

    void ApplyAuthorization(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_token)) return;
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{_token}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    internal async Task<(int Status, string Body)> SendAsync(
        HttpMethod method, string path, string? json = null, bool allowRepair = true)
    {
        try
        {
            using var request = new HttpRequestMessage(method, _baseUrl.TrimEnd('/') + path);
            ApplyAuthorization(request);
            if (json != null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using HttpResponseMessage response = await _http.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            int status = (int)response.StatusCode;

            if (status == (int)HttpStatusCode.Unauthorized && allowRepair && _enablePairing)
            {
                if (await PairAsync())
                {
                    return await SendAsync(method, path, json, allowRepair: false);
                }
            }

            return (status, body);
        }
        catch (TaskCanceledException)
        {
            return (0, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return (0, ex.Message);
        }
    }

    async Task<JsonElement> GetDataAsync(string path)
    {
        (int status, string body) = await SendAsync(HttpMethod.Get, path);
        if (status != (int)HttpStatusCode.OK)
        {
            throw new OpenCodeApiException(status, body);
        }

        using JsonDocument document = JsonDocument.Parse(body);
        if (document.RootElement.TryGetProperty("data", out JsonElement data))
        {
            return data.Clone();
        }
        return default;
    }

    // ---------------------------------------------------------------- helpers

    void Notify(bool force = false)
    {
        long now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastNotifyAt) < NotificationThrottleMs)
        {
            return;
        }

        Interlocked.Exchange(ref _lastNotifyAt, now);

        // Схлопываем частые вызовы: UI обновится один раз на пачку событий.
        if (Interlocked.Exchange(ref _syncQueued, 1) == 1) return;

        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: state notification failed: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _syncQueued, 0);
        }
    }

    internal static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        if (!element.TryGetProperty(property, out JsonElement value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    static string? GetStringOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string Esc(string value) => Uri.EscapeDataString(value);

    internal static string DescribeFailure(string prefix, int status, string body)
    {
        string detail = status switch
        {
            0 => body.Trim().Equals("timeout", StringComparison.OrdinalIgnoreCase)
                ? "OpenCode не отвечает"
                : "нет связи с OpenCode",
            401 => "требуется авторизация",
            404 => "сессия не найдена",
            409 => "сессия занята",
            _ => $"код {status}"
        };

        string trimmed = body.Trim();
        if (trimmed.Length > 300) trimmed = trimmed[..300];
        if (trimmed.Length > 0) App.LogDebug($"OpenCode: {prefix} ({status}): {trimmed}");
        return $"{prefix}: {detail}";
    }

    static string DescribeEventError(JsonElement data)
    {
        if (!data.TryGetProperty("error", out JsonElement error)) return "Ошибка выполнения задачи";

        if (error.ValueKind == JsonValueKind.String) return error.GetString() ?? "Ошибка выполнения задачи";

        string message = GetString(error, "message");
        if (string.IsNullOrEmpty(message)) message = GetString(error, "description");
        if (string.IsNullOrEmpty(message)) message = GetString(error, "type");
        return string.IsNullOrEmpty(message) ? "Ошибка выполнения задачи" : Shorten(message, 140);
    }

    internal static string Shorten(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";
}
