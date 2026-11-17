using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OpenIsland;

public sealed partial class OpenCodeService
{
    // ---------------------------------------------------------------- SSE

    async Task EventStreamLoopAsync(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, _baseUrl.TrimEnd('/') + "/api/event");
                ApplyAuthorization(request);

                using HttpResponseMessage response = await _streamHttp.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellation);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    bool paired = await PairAsync();
                    try
                    {
                        await Task.Delay(paired ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(5), cancellation);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                response.EnsureSuccessStatusCode();
                using Stream stream = await response.Content.ReadAsStreamAsync(cancellation);
                using StreamReader reader = new(stream, Encoding.UTF8);

                while (!cancellation.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(cancellation);
                    if (line == null) break;

                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string json = line[5..].Trim();
                    if (json.Length <= 1) continue;

                    HandleEvent(json);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ex is not HttpRequestException and not IOException)
                {
                    App.LogDebug($"OpenCode: event stream failed: {ex.Message}");
                }
                SetDisconnected("OpenCode офлайн");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal void HandleEvent(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            string type = GetString(root, "type");
            if (type.Length == 0 || !root.TryGetProperty("data", out JsonElement data)) return;

            Interlocked.Exchange(ref _eventsReceived, 1);

            string sessionId = GetString(data, "sessionID");
            bool selected = sessionId.Length > 0 && sessionId == _sessionId;

            switch (type)
            {
                case "server.connected":
                    lock (_gate)
                    {
                        _isConnected = true;
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    break;

                case "session.status":
                {
                    if (!selected) break;
                    string status = "idle";
                    if (data.TryGetProperty("status", out JsonElement statusJson))
                    {
                        status = GetString(statusJson, "type");
                    }
                    lock (_gate)
                    {
                        _isBusy = status != "idle";
                        if (status == "retry")
                        {
                            _lastError = "Повтор запроса к модели...";
                        }
                        else if (status == "idle")
                        {
                            _lastError = "";
                            _taskStartedAtUtc = null;
                        }
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    break;
                }

                case "session.execution.started":
                    if (!selected) break;
                    Interlocked.Increment(ref _executionsStarted);
                    lock (_gate)
                    {
                        _isBusy = true;
                        _streamText.Clear();
                        _streamingMessageActive = false;
                        _lastError = "";
                        _taskStartedAtUtc = DateTime.UtcNow;
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    break;

                case "session.execution.succeeded":
                    if (!selected) break;
                    lock (_gate)
                    {
                        _isBusy = false;
                        _pendingPermission = null;
                        _needMessages = true;
                        if (_streamingMessageActive && _streamText.Length > 0 && _messages.Count > 0 && _messages[^1].Role == "assistant")
                        {
                            List<OpenCodeMessage> messages = [.. _messages];
                            messages[^1] = new OpenCodeMessage("assistant", _streamText.ToString().Trim());
                            Volatile.Write(ref _messages, messages);
                        }
                        _streamingMessageActive = false;
                        Interlocked.Increment(ref _messagesRevision);
                        _taskStartedAtUtc = null;
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    _ = RefreshAsync();
                    break;

                case "session.execution.failed":
                    if (!selected) break;
                    lock (_gate)
                    {
                        _isBusy = false;
                        _pendingPermission = null;
                        _needMessages = true;
                        if (_streamingMessageActive && _streamText.Length > 0 && _messages.Count > 0 && _messages[^1].Role == "assistant")
                        {
                            List<OpenCodeMessage> messages = [.. _messages];
                            messages[^1] = new OpenCodeMessage("assistant", _streamText.ToString().Trim());
                            Volatile.Write(ref _messages, messages);
                        }
                        _streamingMessageActive = false;
                        Interlocked.Increment(ref _messagesRevision);
                        _lastError = DescribeEventError(data);
                        _taskStartedAtUtc = null;
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    _ = RefreshAsync();
                    break;

                case "session.text.started":
                    if (!selected) break;
                    lock (_gate)
                    {
                        _streamText.Clear();
                        _streamingMessageActive = true;
                        List<OpenCodeMessage> messages = [.. _messages];
                        messages.Add(new OpenCodeMessage("assistant", ""));
                        Volatile.Write(ref _messages, messages);
                        Interlocked.Increment(ref _messagesRevision);
                    }
                    break;

                case "session.text.delta":
                {
                    if (!selected) break;
                    string delta = GetString(data, "delta");
                    if (delta.Length == 0) break;
                    lock (_gate)
                    {
                        if (!_streamingMessageActive)
                        {
                            _streamText.Clear();
                            _streamingMessageActive = true;
                            List<OpenCodeMessage> messages = [.. _messages];
                            messages.Add(new OpenCodeMessage("assistant", ""));
                            Volatile.Write(ref _messages, messages);
                        }
                        if (_streamText.Length > 12000) _streamText.Remove(0, _streamText.Length - 12000);
                        _streamText.Append(delta);
                        _lastAssistantText = _streamText.ToString();
                        if (_streamingMessageActive && _messages.Count > 0 && _messages[^1].Role == "assistant")
                        {
                            List<OpenCodeMessage> messages = [.. _messages];
                            messages[^1] = new OpenCodeMessage("assistant", _lastAssistantText);
                            Volatile.Write(ref _messages, messages);
                        }
                        Interlocked.Increment(ref _messagesRevision);
                    }
                    Notify();
                    break;
                }

                case "session.text.ended":
                    if (!selected) break;
                    lock (_gate)
                    {
                        string text = _streamText.ToString().Trim();
                        if (text.Length > 0)
                        {
                            _lastAssistantText = text;
                            if (_streamingMessageActive && _messages.Count > 0 && _messages[^1].Role == "assistant")
                            {
                                List<OpenCodeMessage> messages = [.. _messages];
                                messages[^1] = new OpenCodeMessage("assistant", text);
                                Volatile.Write(ref _messages, messages);
                            }
                        }
                        _streamingMessageActive = false;
                        Interlocked.Increment(ref _messagesRevision);
                    }
                    Notify(force: true);
                    break;

                case "session.text.finished":
                    if (!selected) break;
                    lock (_gate)
                    {
                        string text = _streamText.ToString().Trim();
                        if (text.Length > 0 && _streamingMessageActive && _messages.Count > 0 && _messages[^1].Role == "assistant")
                        {
                            List<OpenCodeMessage> messages = [.. _messages];
                            messages[^1] = new OpenCodeMessage("assistant", text);
                            Volatile.Write(ref _messages, messages);
                            _lastAssistantText = text;
                        }
                        _streamText.Clear();
                        _streamingMessageActive = false;
                        Interlocked.Increment(ref _messagesRevision);
                    }
                    Notify(force: true);
                    break;

                case "permission.asked":
                {
                    if (!selected) break;
                    string id = GetString(data, "id");
                    if (id.Length == 0) break;

                    List<string> resources = [];
                    if (data.TryGetProperty("resources", out JsonElement resourcesJson)
                        && resourcesJson.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement resource in resourcesJson.EnumerateArray())
                        {
                            string? value = GetStringOrNull(resource);
                            if (!string.IsNullOrEmpty(value)) resources.Add(value);
                        }
                    }

                    lock (_gate)
                    {
                        _pendingPermission = new OpenCodePermissionInfo(
                            id, sessionId, GetString(data, "action"),
                            GetString(data, "message"), resources);
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    break;
                }

                case "permission.replied":
                {
                    string id = GetString(data, "id");
                    if (_pendingPermission == null || _pendingPermission.Id != id) break;
                    lock (_gate)
                    {
                        _pendingPermission = null;
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    break;
                }

                case "session.model.selected":
                    if (!selected) break;
                    if (data.TryGetProperty("model", out JsonElement model))
                    {
                        string modelId = GetString(model, "id");
                        string providerId = GetString(model, "providerID");
                        lock (_gate)
                        {
                            _sessionModelId = modelId;
                            _sessionModelProviderId = providerId;
                            _modelName = DescribeModel(modelId, providerId, Models);
                        }
                        Notify(force: true);
                    }
                    break;

                case "session.created":
                case "session.deleted":
                case "session.renamed":
                case "session.moved":
                case "session.archived":
                case "session.unarchived":
                    _ = RefreshAsync();
                    break;
            }
        }
        catch (JsonException)
        {
            // Частичный или незнакомый кадр — пропускаем.
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: event handling failed: {ex.Message}");
        }
    }

}
