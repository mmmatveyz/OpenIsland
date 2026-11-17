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
                    Interlocked.Increment(ref _messagesLoadGeneration);
                    _messagesLoaded = false;
                    ResetMessagePaging();
                    Volatile.Write(ref _messages, []);
                    Interlocked.Increment(ref _messagesRevision);
                    _streamText.Clear();
                    _streamingMessageActive = false;
                    _lastAssistantText = "";
                    _sessionTitle = null;
                    _sessionDirectory = null;
                    _isBusy = false;
                    ResetDiffState();
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
                            Interlocked.Increment(ref _messagesLoadGeneration);
                            _messagesLoaded = false;
                            ResetMessagePaging();
                            Volatile.Write(ref _messages, []);
                            Interlocked.Increment(ref _messagesRevision);
                            _needMessages = true;
                            _pendingPermission = null;
                            ResetDiffState();
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

            string? sessionId;
            bool shouldLoadMessages;
            lock (_gate)
            {
                sessionId = _sessionId;
                shouldLoadMessages = sessionId != null && (_needMessages || !_messagesLoaded);
            }
            if (shouldLoadMessages && sessionId != null) await LoadMessagesAsync(sessionId);

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
        long generation = Interlocked.Read(ref _messagesLoadGeneration);
        try
        {
            OpenCodePage page = await GetPageAsync(
                $"/api/session/{Esc(sessionId)}/message?limit={MessagePageSize}&order=desc");
            List<OpenCodeMessage> messages = ParseMessages(page.Data);
            messages.Reverse();
            lock (_gate)
            {
                if (_sessionId != sessionId || generation != Interlocked.Read(ref _messagesLoadGeneration)) return;
                messages = MergeMessages(messages, _messages);
                Volatile.Write(ref _messages, messages);
                // Курсор берём только с первой загрузки сессии: при обновлении после задачи
                // нельзя откатывать уже догруженную историю к самому свежему краю.
                if (!_messagesLoaded)
                {
                    _olderCursor = page.NextCursor.Length > 0 ? page.NextCursor : null;
                    _hasOlderMessages = _olderCursor != null;
                }
                _messagesLoaded = true;
                _needMessages = false;
                if (!_isBusy)
                {
                    _streamText.Clear();
                    _streamingMessageActive = false;
                }
                Interlocked.Increment(ref _messagesRevision);
                OpenCodeMessage? lastAssistant = messages.LastOrDefault(message => message.Role == "assistant");
                if (!_streamingMessageActive && lastAssistant != null) _lastAssistantText = lastAssistant.Text;
            }
            Notify(force: true);
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: message load failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Догружает страницу сообщений старше показанных — вызывается при прокрутке
    /// истории вверх. Курсор — из ответа <c>cursor.next</c> (в порядке desc он ведёт вглубь).
    /// </summary>
    public async Task<bool> LoadOlderMessagesAsync()
    {
        string? sessionId;
        string? cursor;
        lock (_gate)
        {
            sessionId = _sessionId;
            cursor = _olderCursor;
            if (sessionId == null || string.IsNullOrEmpty(cursor) || _loadingOlderMessages) return false;
            _loadingOlderMessages = true;
        }
        Notify(force: true);

        try
        {
            OpenCodePage page = await GetPageAsync(
                $"/api/session/{Esc(sessionId)}/message?limit={MessagePageSize}&cursor={Esc(cursor)}");
            List<OpenCodeMessage> older = ParseMessages(page.Data);
            older.Reverse();

            lock (_gate)
            {
                _loadingOlderMessages = false;
                if (_sessionId != sessionId) return false;

                _olderCursor = page.NextCursor.Length > 0 ? page.NextCursor : null;
                _hasOlderMessages = _olderCursor != null;
                if (older.Count > 0) Volatile.Write(ref _messages, MergeOlder(older, _messages));
                // Ревизия растёт и на пустой странице: интерфейс должен спрятать кнопку догрузки.
                Interlocked.Increment(ref _messagesRevision);
            }

            Notify(force: true);
            return older.Count > 0;
        }
        catch (Exception ex)
        {
            lock (_gate) _loadingOlderMessages = false;
            App.LogDebug($"OpenCode: older messages load failed: {ex.Message}");
            Notify(force: true);
            return false;
        }
    }

    /// <summary>
    /// Свежая страница (последние <see cref="MessagePageSize"/> сообщений) плюс уже показанное:
    /// сохраняем догруженный старый префикс и живой хвост из SSE, тело берём из страницы.
    /// </summary>
    internal static List<OpenCodeMessage> MergeMessages(
        IReadOnlyList<OpenCodeMessage> newest,
        IReadOnlyList<OpenCodeMessage> current)
    {
        if (newest.Count == 0) return TrimMessages([.. current]);

        int first = IndexOfMessage(current, newest[0]);
        if (first < 0)
        {
            // Пересечения нет — доверяем странице, но не теряем живой ответ из SSE.
            List<OpenCodeMessage> fresh = [.. newest];
            foreach (OpenCodeMessage message in current)
            {
                if (!fresh.Contains(message)) fresh.Add(message);
            }
            return TrimMessages(fresh);
        }

        int last = IndexOfMessage(current, newest[^1]);
        List<OpenCodeMessage> merged = [.. current.Take(first), .. newest];
        if (last >= 0)
        {
            foreach (OpenCodeMessage message in current.Skip(last + 1))
            {
                if (!merged.Contains(message)) merged.Add(message);
            }
        }
        return TrimMessages(merged);
    }

    /// <summary>Ставит страницу старой истории перед уже показанной, убирая дубли на стыке.</summary>
    internal static List<OpenCodeMessage> MergeOlder(
        IReadOnlyList<OpenCodeMessage> older,
        IReadOnlyList<OpenCodeMessage> current)
    {
        List<OpenCodeMessage> merged = [.. older];
        foreach (OpenCodeMessage message in current)
        {
            if (!merged.Contains(message)) merged.Add(message);
        }
        return TrimMessages(merged, keepOldest: true);
    }

    static int IndexOfMessage(IReadOnlyList<OpenCodeMessage> list, OpenCodeMessage message)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id.Length > 0 ? list[i].Id == message.Id : list[i].Equals(message)) return i;
        }
        return -1;
    }

    static List<OpenCodeMessage> TrimMessages(List<OpenCodeMessage> messages, bool keepOldest = false)
    {
        if (messages.Count <= MaxMessages) return messages;

        // Догрузка старых страниц не должна выбрасывать только что загруженное:
        // в этом случае жертвуем самым свежим краем, к которому можно вернуться обновлением.
        if (keepOldest) messages.RemoveRange(MaxMessages, messages.Count - MaxMessages);
        else messages.RemoveRange(0, messages.Count - MaxMessages);
        return messages;
    }

    /// <summary>Сбрасывает постраничную историю при смене или удалении сессии.</summary>
    void ResetMessagePaging()
    {
        _olderCursor = null;
        _hasOlderMessages = false;
        _loadingOlderMessages = false;
    }

    internal static List<OpenCodeMessage> ParseMessages(JsonElement data)
    {
        List<OpenCodeMessage> messages = [];
        if (data.ValueKind != JsonValueKind.Array) return messages;

        foreach (JsonElement item in data.EnumerateArray())
        {
            string role = GetString(item, "role");
            if (string.IsNullOrEmpty(role) && item.TryGetProperty("info", out JsonElement info))
            {
                role = GetString(info, "role");
            }
            // OpenCode V2 кладёт роль сообщения в поле "type" (user/assistant),
            // а не в "role"/"info.role"; непроекционные события (idle, compaction,
            // model-switched и т.п.) тоже приходят как элементы истории.
            if (string.IsNullOrEmpty(role)) role = GetString(item, "type");
            if (role is not ("user" or "assistant")) continue;

            StringBuilder text = new();
            List<string> tools = [];
            if (item.TryGetProperty("parts", out JsonElement parts) && parts.ValueKind == JsonValueKind.Array)
            {
                CollectParts(parts, text, tools);
            }
            else if (item.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
            {
                CollectParts(content, text, tools);
            }

            // В V2 сообщение пользователя приходит без "parts"/"content": его текст
            // лежит в "text" (дублируется в "metadata.displayText"), а вложения —
            // в "files" (base64, в интерфейсе не показываем и не храним).
            if (text.Length == 0) text.Append(GetString(item, "text"));
            if (text.Length == 0 && item.TryGetProperty("metadata", out JsonElement metadata)
                && metadata.ValueKind == JsonValueKind.Object)
            {
                text.Append(GetString(metadata, "displayText"));
            }

            string value = text.ToString().Trim();
            // Ход без текста — это работа агента: показываем её компактной строкой,
            // иначе история сессии выглядит почти пустой, хотя сообщений много.
            if (value.Length == 0 && tools.Count > 0)
            {
                role = "tool";
                value = DescribeTools(tools);
            }
            if (value.Length == 0
                && item.TryGetProperty("files", out JsonElement files)
                && files.ValueKind == JsonValueKind.Array
                && files.GetArrayLength() > 0)
            {
                value = "[вложение]";
            }
            if (value.Length == 0) continue;
            if (value.Length > 12000) value = value[..12000];
            messages.Add(new OpenCodeMessage(role, value, GetString(item, "id")));
        }

        return messages;
    }

    static readonly string[] ToolDetailKeys = ["command", "filePath", "path", "pattern", "description", "query", "url"];

    static void CollectParts(JsonElement parts, StringBuilder text, List<string> tools)
    {
        foreach (JsonElement part in parts.EnumerateArray())
        {
            string type = GetString(part, "type");
            if (type == "text")
            {
                text.Append(GetString(part, "text"));
                continue;
            }
            if (type != "tool") continue;

            string name = GetString(part, "name");
            if (name.Length == 0) continue;
            string detail = ToolDetail(part);
            tools.Add(detail.Length > 0 ? $"{name}: {detail}" : name);
        }
    }

    /// <summary>Короткая суть вызова инструмента: команда, путь или шаблон поиска.</summary>
    static string ToolDetail(JsonElement part)
    {
        if (!part.TryGetProperty("state", out JsonElement state) || state.ValueKind != JsonValueKind.Object) return "";
        if (!state.TryGetProperty("input", out JsonElement input) || input.ValueKind != JsonValueKind.Object) return "";

        foreach (string key in ToolDetailKeys)
        {
            string value = GetString(input, key);
            if (value.Length > 0) return ShortenLine(value, 48);
        }
        return "";
    }

    static string DescribeTools(IReadOnlyList<string> tools)
    {
        StringBuilder description = new("⚙ ");
        for (int i = 0; i < tools.Count; i++)
        {
            if (i > 0) description.Append(" · ");
            description.Append(tools[i]);
            if (description.Length > 150)
            {
                description.Length = 150;
                description.Append('…');
                break;
            }
        }
        return description.ToString();
    }

    static string ShortenLine(string value, int limit)
    {
        value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (value.Length <= limit) return value;
        return value[..limit] + "…";
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
