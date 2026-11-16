using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OpenIsland;

public sealed partial class OpenCodeService
{
    // ---------------------------------------------------------------- state refresh

    public async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1) return;

        try
        {
            if (!_isConnected)
            {
                await DetectServiceUrlAsync(force: false);
            }

            JsonElement active = await GetDataAsync("/api/session/active");
            JsonElement sessionsJson = await GetDataAsync("/api/session");
            JsonElement modelsJson = await GetDataAsync("/api/model");

            HashSet<string> running = ParseRunningSessions(active);
            List<OpenCodeSessionInfo> sessions = ParseSessions(sessionsJson, running);
            List<OpenCodeModelInfo> models = ParseModels(modelsJson);

            lock (_gate)
            {
                Volatile.Write(ref _sessions, sessions);
                Volatile.Write(ref _models, models);

                if (sessions.Count == 0)
                {
                    _sessionId = null;
                    _sessionTitle = null;
                    _sessionDirectory = null;
                    _isBusy = false;
                }
                else
                {
                    // Never silently replace an explicitly selected session that has not
                    // appeared in the list yet (eventual consistency after create/switch).
                    OpenCodeSessionInfo? selected = _sessionId != null
                        ? sessions.FirstOrDefault(s => s.Id == _sessionId)
                        : sessions.FirstOrDefault(s => s.IsRunning) ?? sessions[0];

                    if (selected != null)
                    {
                        if (selected.Id != _sessionId)
                        {
                            _sessionId = selected.Id;
                            _needMessages = true;
                            _pendingPermission = null;
                        }

                        _sessionTitle = selected.Title;
                        _sessionDirectory = selected.Directory;
                        _sessionModelId = selected.ModelId;
                        _sessionModelProviderId = selected.ModelProviderId;
                        if (_isBusy && !selected.IsRunning)
                        {
                            _needMessages = true;
                        }
                        _isBusy = selected.IsRunning;
                        if (!_isBusy) _taskStartedAtUtc = null;
                        _modelName = DescribeModel(selected.ModelId, selected.ModelProviderId, models);
                    }
                }

                if (!_isConnected)
                {
                    _isConnected = true;
                    _lastError = "";
                    App.LogDebug($"OpenCode: connected to {_baseUrl}.");
                }

                UpdateStatusText();
            }

            if (_needMessages && _sessionId != null)
            {
                _needMessages = false;
                await LoadMessagesAsync(_sessionId);
            }

            if (_sessionId != null && (_isBusy || _pendingPermission != null))
            {
                await RefreshPermissionAsync(_sessionId);
            }

            Notify(force: true);
        }
        catch (OpenCodeApiException ex) when (ex.Status == 401)
        {
            SetDisconnected("Требуется авторизация OpenCode");
        }
        catch (Exception ex)
        {
            SetDisconnected(ex is OpenCodeApiException ? "Ошибка OpenCode" : "OpenCode офлайн");
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    internal static HashSet<string> ParseRunningSessions(JsonElement active)
    {
        HashSet<string> running = [];
        if (active.ValueKind != JsonValueKind.Object) return running;

        foreach (JsonProperty property in active.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object
                && GetString(property.Value, "type") == "running")
            {
                running.Add(property.Name);
            }
        }
        return running;
    }

    internal static List<OpenCodeSessionInfo> ParseSessions(JsonElement data, HashSet<string> running)
    {
        List<OpenCodeSessionInfo> sessions = [];
        if (data.ValueKind != JsonValueKind.Array) return sessions;

        foreach (JsonElement session in data.EnumerateArray())
        {
            string id = GetString(session, "id");
            if (string.IsNullOrEmpty(id)) continue;

            string title = GetString(session, "title");
            if (string.IsNullOrWhiteSpace(title)) title = "Без названия";

            string directory = "";
            if (session.TryGetProperty("location", out JsonElement location)
                && location.ValueKind == JsonValueKind.Object)
            {
                directory = GetString(location, "directory");
            }

            string modelId = "";
            string providerId = "";
            if (session.TryGetProperty("model", out JsonElement model)
                && model.ValueKind == JsonValueKind.Object)
            {
                modelId = GetString(model, "id");
                providerId = GetString(model, "providerID");
            }

            long updatedAt = 0;
            if (session.TryGetProperty("time", out JsonElement time)
                && time.ValueKind == JsonValueKind.Object
                && time.TryGetProperty("updated", out JsonElement updated)
                && updated.TryGetInt64(out long parsed))
            {
                updatedAt = parsed;
            }

            sessions.Add(new OpenCodeSessionInfo(
                id, title, directory, modelId, providerId, running.Contains(id), updatedAt));
        }

        return sessions;
    }

    internal static List<OpenCodeModelInfo> ParseModels(JsonElement data)
    {
        List<OpenCodeModelInfo> models = [];
        if (data.ValueKind != JsonValueKind.Array) return models;

        foreach (JsonElement model in data.EnumerateArray())
        {
            string id = GetString(model, "id");
            if (string.IsNullOrEmpty(id)) continue;

            if (model.TryGetProperty("enabled", out JsonElement enabled)
                && enabled.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            string name = GetString(model, "name");
            if (string.IsNullOrWhiteSpace(name)) name = id;

            models.Add(new OpenCodeModelInfo(
                id, name, GetString(model, "providerID")));
        }

        return models;
    }

    internal static string DescribeModel(string modelId, string providerId, IReadOnlyList<OpenCodeModelInfo> models)
    {
        if (string.IsNullOrEmpty(modelId)) return "Модель не выбрана";

        foreach (OpenCodeModelInfo model in models)
        {
            if (model.Id == modelId && (string.IsNullOrEmpty(providerId) || model.ProviderId == providerId))
            {
                return Shorten(model.Name, 26);
            }
        }

        return Shorten(modelId, 26);
    }

    void SetDisconnected(string reason)
    {
        bool changed = _isConnected || _statusText != reason;
        _isConnected = false;
        _isBusy = false;
        _statusText = reason;
        if (changed) App.LogDebug($"OpenCode: {reason}.");
        Notify(force: true);
    }

    void UpdateStatusText()
    {
        if (!_isConnected)
        {
            _statusText = "OpenCode офлайн";
            return;
        }

        if (_pendingPermission != null)
        {
            _statusText = "Требуется разрешение";
            return;
        }

        if (_isBusy)
        {
            _statusText = string.IsNullOrWhiteSpace(_sessionTitle)
                ? "Агент работает..."
                : $"Агент: {Shorten(_sessionTitle!, 22)}";
            return;
        }

        _statusText = "OpenCode готов";
    }

    // ---------------------------------------------------------------- messages

    async Task LoadMessagesAsync(string sessionId)
    {
        try
        {
            JsonElement data = await GetDataAsync($"/api/session/{Esc(sessionId)}/message?limit=8&order=desc");
            if (data.ValueKind != JsonValueKind.Array) return;

            foreach (JsonElement message in data.EnumerateArray())
            {
                if (GetString(message, "type") != "assistant") continue;
                if (!message.TryGetProperty("content", out JsonElement content)
                    || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                StringBuilder text = new();
                foreach (JsonElement part in content.EnumerateArray())
                {
                    if (GetString(part, "type") == "text")
                    {
                        text.Append(GetString(part, "text"));
                    }
                }

                string result = text.ToString().Trim();
                if (result.Length > 0)
                {
                    if (result.Length > 12000) result = result[..12000];
                    lock (_gate) _lastAssistantText = result;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: message load failed: {ex.Message}");
        }
    }

    async Task RefreshPermissionAsync(string sessionId)
    {
        try
        {
            JsonElement data = await GetDataAsync($"/api/session/{Esc(sessionId)}/permission");
            if (data.ValueKind != JsonValueKind.Array) return;

            OpenCodePermissionInfo? first = null;
            foreach (JsonElement item in data.EnumerateArray())
            {
                string id = GetString(item, "id");
                if (string.IsNullOrEmpty(id)) continue;

                List<string> resources = [];
                if (item.TryGetProperty("resources", out JsonElement resourcesJson)
                    && resourcesJson.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement resource in resourcesJson.EnumerateArray())
                    {
                        string? value = GetStringOrNull(resource);
                        if (!string.IsNullOrEmpty(value)) resources.Add(value);
                    }
                }

                first = new OpenCodePermissionInfo(
                    id,
                    GetString(item, "sessionID"),
                    GetString(item, "action"),
                    GetString(item, "message"),
                    resources);
                break;
            }

            bool changed = first?.Id != _pendingPermission?.Id;
            _pendingPermission = first;
            if (changed)
            {
                UpdateStatusText();
                Notify(force: true);
            }
        }
        catch (Exception ex)
        {
            // Эндпоинт может возвращать 404, пока агент не запрашивал разрешение.
            App.LogDebug($"OpenCode: permission refresh failed: {ex.Message}");
        }
    }

}
