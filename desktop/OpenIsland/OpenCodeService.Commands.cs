using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OpenIsland;

public sealed partial class OpenCodeService
{
    // ---------------------------------------------------------------- commands

    public async Task<bool> SendPromptAsync(string text)
    {
        string prompt = text.Trim();
        if (prompt.Length == 0) return false;

        string? sessionId = _sessionId;
        if (sessionId == null)
        {
            if (!await CreateSessionAsync("OpenIsland — новая задача")) return false;
            sessionId = _sessionId;
            if (sessionId == null) return false;
        }

        string payload = JsonSerializer.Serialize(new { text = prompt });
        (int status, string body) = await SendAsync(
            HttpMethod.Post, $"/api/session/{Esc(sessionId)}/prompt", payload);

        if (status is >= 200 and < 300)
        {
            lock (_gate)
            {
                _streamText.Clear();
                _streamingMessageActive = false;
                _lastAssistantText = "";
                List<OpenCodeMessage> messages = [.. _messages];
                messages.Add(new OpenCodeMessage("user", prompt));
                Volatile.Write(ref _messages, messages);
                Interlocked.Increment(ref _messagesRevision);
                _lastError = "";
                _isBusy = true;
                _needMessages = false;
                _pendingPermission = null;
                _taskStartedAtUtc = DateTime.UtcNow;
                UpdateStatusText();
            }
            Notify(force: true);
            return true;
        }

        lock (_gate)
        {
            _lastError = DescribeFailure("Не удалось отправить задачу", status, body);
            UpdateStatusText();
        }
        Notify(force: true);
        return false;
    }

    public async Task<bool> CreateSessionAsync(string? title = null, string? directory = null)
    {
        string targetDirectory = directory ?? _sessionDirectory ?? "";
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            targetDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        Dictionary<string, object?> payload = new()
        {
            ["title"] = string.IsNullOrWhiteSpace(title) ? "OpenIsland — новая задача" : title,
            ["location"] = new { directory = targetDirectory }
        };

        OpenCodeModelInfo? preferred = SelectedModelIndex >= 0 && SelectedModelIndex < Models.Count
            ? Models[SelectedModelIndex]
            : null;
        if (preferred != null)
        {
            payload["model"] = new { id = preferred.Id, providerID = preferred.ProviderId };
        }

        (int status, string body) = await SendAsync(
            HttpMethod.Post, "/api/session", JsonSerializer.Serialize(payload));

        if (status is < 200 or >= 300)
        {
            lock (_gate)
            {
                _lastError = DescribeFailure("Не удалось создать сессию", status, body);
                UpdateStatusText();
            }
            Notify(force: true);
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement data = document.RootElement.TryGetProperty("data", out JsonElement value)
                ? value
                : document.RootElement;

            string id = GetString(data, "id");
            if (string.IsNullOrEmpty(id)) return false;

            lock (_gate)
            {
                _sessionId = id;
                Interlocked.Increment(ref _messagesLoadGeneration);
                _messagesLoaded = false;
                _sessionTitle = GetString(data, "title");
                _sessionDirectory = targetDirectory;
                _isBusy = false;
                _pendingPermission = null;
                _streamText.Clear();
                _streamingMessageActive = false;
                _lastAssistantText = "";
                Volatile.Write(ref _messages, []);
                Interlocked.Increment(ref _messagesRevision);
                _lastError = "";
                UpdateStatusText();
            }

            Notify(force: true);
            await RefreshAsync();
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: session create parse failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Выбирает сессию по id. Возвращает false, если такая сессия не найдена.</summary>
    public bool SelectSession(string sessionId)
    {
        OpenCodeSessionInfo? selected = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (selected == null) return false;

        lock (_gate)
        {
            _sessionId = selected.Id;
            Interlocked.Increment(ref _messagesLoadGeneration);
            _messagesLoaded = false;
            _sessionTitle = selected.Title;
            _sessionDirectory = selected.Directory;
            _sessionModelId = selected.ModelId;
            _sessionModelProviderId = selected.ModelProviderId;
            _isBusy = selected.IsRunning;
            _pendingPermission = null;
            _streamText.Clear();
            _streamingMessageActive = false;
            _lastAssistantText = "";
            Volatile.Write(ref _messages, []);
            Interlocked.Increment(ref _messagesRevision);
            _lastError = "";
            _needMessages = true;
            ResetDiffState();
            UpdateStatusText();
        }

        Notify(force: true);
        _ = RefreshAsync();
        return true;
    }

    public void SelectSession(int direction)
    {
        IReadOnlyList<OpenCodeSessionInfo> sessions = Sessions;
        if (sessions.Count == 0) return;

        int index = SelectedSessionIndex;
        index = index < 0 ? 0 : (index + direction + sessions.Count) % sessions.Count;

        lock (_gate)
        {
            OpenCodeSessionInfo selected = sessions[index];
            _sessionId = selected.Id;
            Interlocked.Increment(ref _messagesLoadGeneration);
            _messagesLoaded = false;
            _sessionTitle = selected.Title;
            _sessionDirectory = selected.Directory;
            _sessionModelId = selected.ModelId;
            _sessionModelProviderId = selected.ModelProviderId;
            _isBusy = selected.IsRunning;
            _pendingPermission = null;
            _streamText.Clear();
            _streamingMessageActive = false;
            _lastAssistantText = "";
            Volatile.Write(ref _messages, []);
            Interlocked.Increment(ref _messagesRevision);
            _lastError = "";
            _needMessages = true;
            ResetDiffState();
            UpdateStatusText();
        }

        Notify(force: true);
        _ = RefreshAsync();
    }

    public async Task CycleModelAsync(int direction)
    {
        IReadOnlyList<OpenCodeModelInfo> models = Models;
        if (models.Count == 0 || _sessionId == null) return;

        int index = SelectedModelIndex;
        index = index < 0 ? 0 : (index + direction + models.Count) % models.Count;
        OpenCodeModelInfo model = models[index];
        await ApplyModelAsync(model.Id, model.ProviderId, model.Name);
    }

    /// <summary>Выбирает модель по ссылке вида "provider/id" или по простому id.</summary>
    public async Task<bool> SelectModelAsync(string modelRef)
    {
        if (string.IsNullOrWhiteSpace(modelRef)) return false;

        IReadOnlyList<OpenCodeModelInfo> models = Models;
        OpenCodeModelInfo? match = models.FirstOrDefault(m =>
            string.Equals(m.Id, modelRef, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"{m.ProviderId}/{m.Id}", modelRef, StringComparison.OrdinalIgnoreCase));

        string id = match?.Id ?? modelRef;
        string provider = match?.ProviderId ?? "";
        return await ApplyModelAsync(id, provider, match?.Name ?? id);
    }

    async Task<bool> ApplyModelAsync(string modelId, string providerId, string displayName)
    {
        string? sessionId = _sessionId;
        if (sessionId == null) return false;

        string payload = JsonSerializer.Serialize(new
        {
            model = new { id = modelId, providerID = providerId }
        });

        (int status, string body) = await SendAsync(
            HttpMethod.Post, $"/api/session/{Esc(sessionId)}/model", payload);

        bool ok = status is >= 200 and < 300;
        lock (_gate)
        {
            if (ok)
            {
                _sessionModelId = modelId;
                _sessionModelProviderId = providerId;
                _modelName = Shorten(displayName, 26);
                _lastError = "";
            }
            else
            {
                _lastError = DescribeFailure("Не удалось сменить модель", status, body);
            }
            UpdateStatusText();
        }

        Notify(force: true);
        return ok;
    }

    public async Task<bool> InterruptAsync()
    {
        string? sessionId = _sessionId;
        if (sessionId == null) return false;

        (int status, _) = await SendAsync(
            HttpMethod.Post, $"/api/session/{Esc(sessionId)}/interrupt", "{}");

        if (status is >= 200 and < 300)
        {
            lock (_gate)
            {
                _isBusy = false;
                UpdateStatusText();
            }
            Notify(force: true);
            return true;
        }

        return false;
    }

    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        (int status, _) = await SendAsync(HttpMethod.Delete, $"/api/session/{Esc(sessionId)}");
        if (status is < 200 or >= 300) return false;

        lock (_gate)
        {
            if (_sessionId == sessionId)
            {
                _sessionId = null;
                Interlocked.Increment(ref _messagesLoadGeneration);
                _messagesLoaded = false;
                _streamingMessageActive = false;
                Volatile.Write(ref _messages, []);
                Interlocked.Increment(ref _messagesRevision);
                _lastAssistantText = "";
                _sessionTitle = null;
                _sessionDirectory = null;
                _isBusy = false;
                _taskStartedAtUtc = null;
                _pendingPermission = null;
                _lastAssistantText = "";
                _needMessages = false;
                ResetDiffState();
            }
            UpdateStatusText();
        }

        Notify(force: true);
        return true;
    }

    public async Task<bool> ReplyPermissionAsync(string decision)
    {
        OpenCodePermissionInfo? pending = _pendingPermission;
        if (pending == null) return false;

        string payload = JsonSerializer.Serialize(new { decision });
        (int status, string body) = await SendAsync(
            HttpMethod.Post,
            $"/api/session/{Esc(pending.SessionId)}/permission/{Esc(pending.Id)}/reply",
            payload);

        if (status is >= 200 and < 300)
        {
            lock (_gate)
            {
                if (_pendingPermission?.Id == pending.Id) _pendingPermission = null;
                UpdateStatusText();
            }
            Notify(force: true);
            return true;
        }

        lock (_gate)
        {
            _lastError = DescribeFailure("Не удалось ответить на запрос", status, body);
        }
        Notify(force: true);
        return false;
    }

    public void OpenApp()
    {
        try
        {
            string[] candidates =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "@opencode-aidesktop", "OpenCode.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "opencode", "OpenCode.exe")
            ];

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate)) continue;
                Process.Start(new ProcessStartInfo(candidate) { UseShellExecute = true });
                return;
            }

            Process.Start(new ProcessStartInfo(_baseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: open app failed: {ex.Message}");
        }
    }

}
