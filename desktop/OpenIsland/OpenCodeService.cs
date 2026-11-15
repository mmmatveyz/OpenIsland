using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace OpenIsland;

public sealed record OpenCodeSessionInfo(
    string Id,
    string Title,
    string Directory,
    string ModelId,
    string ModelProviderId,
    bool IsRunning,
    long UpdatedAt);

public sealed record OpenCodeModelInfo(string Id, string Name, string ProviderId);

public sealed record OpenCodePermissionInfo(
    string Id,
    string SessionId,
    string Action,
    string? Message,
    IReadOnlyList<string> Resources);

/// <summary>
/// Клиент локального демона OpenCode (REST v2 + SSE).
/// Вся работа с сетью идёт в фоне; UI подписывается на <see cref="StateChanged"/>.
/// </summary>
public sealed partial class OpenCodeService : IDisposable
{
    const string DefaultBaseUrl = "http://127.0.0.1:28894";
    const int PollingIntervalMs = 2000;
    const int CliTimeoutMs = 4000;
    const int NotificationThrottleMs = 120;

    static readonly byte[] TokenEntropy = Encoding.UTF8.GetBytes("OpenIsland.OpenCode.v1");

    static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenIsland");
    static readonly string TokenFile = Path.Combine(DataDirectory, "opencode-token.bin");
    static readonly string LegacyTokenFile = Path.Combine(DataDirectory, "opencode-token.txt");

    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly HttpClient _streamHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(PollingIntervalMs) };

    readonly object _gate = new();
    readonly StringBuilder _streamText = new();

    List<OpenCodeSessionInfo> _sessions = [];
    List<OpenCodeModelInfo> _models = [];

    string _baseUrl = DefaultBaseUrl;
    string? _token;
    string? _sessionId;
    string? _sessionTitle;
    string? _sessionDirectory;
    string _sessionModelId = "";
    string _sessionModelProviderId = "";
    string _modelName = "Модель не выбрана";
    string _lastAssistantText = "";
    string _lastError = "";
    string _statusText = "Поиск OpenCode...";
    OpenCodePermissionInfo? _pendingPermission;

    bool _isConnected;
    bool _isBusy;
    bool _needMessages;
    int _refreshing;
    int _syncQueued;
    int _eventsReceived;
    long _executionsStarted;
    long _lastNotifyAt;
    long _lastDiscoveryAt;
    DateTime? _taskStartedAtUtc;
    CancellationTokenSource? _streamCancellation;

    public OpenCodeService()
    {
        LoadCachedToken();
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();
        _ = Task.Run(InitializeAsync);
    }

    public event Action? StateChanged;
    public bool IsConnected => _isConnected;
    public bool IsBusy => _isBusy;
    public DateTime? TaskStartedAtUtc { get { lock (_gate) return _taskStartedAtUtc; } }
    public string StatusText => _statusText;
    public string SessionTitle => _sessionTitle ?? "";
    public string ModelName => _modelName;
    public string LastAssistantText => _lastAssistantText;
    public string LastError => _lastError;
    public string? SelectedSessionId => _sessionId;
    public string SessionDirectory => _sessionDirectory ?? "";
    public OpenCodePermissionInfo? PendingPermission => _pendingPermission;
    public IReadOnlyList<OpenCodeSessionInfo> Sessions => Volatile.Read(ref _sessions);
    public IReadOnlyList<OpenCodeModelInfo> Models => Volatile.Read(ref _models);

    /// <summary>Сколько событий получено по SSE — используется диагностикой.</summary>
    public bool EventsReceived => Volatile.Read(ref _eventsReceived) > 0;

    /// <summary>Сколько раз демон подтверждал старт выполнения задачи (SSE).</summary>
    public long ExecutionStartedCount => Interlocked.Read(ref _executionsStarted);

    public int SelectedSessionIndex
    {
        get
        {
            IReadOnlyList<OpenCodeSessionInfo> list = Sessions;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == _sessionId) return i;
            }
            return list.Count > 0 ? 0 : -1;
        }
    }

    public int SelectedModelIndex
    {
        get
        {
            IReadOnlyList<OpenCodeModelInfo> list = Models;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == _sessionModelId && list[i].ProviderId == _sessionModelProviderId) return i;
            }
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id == _sessionModelId) return i;
            }
            return list.Count > 0 ? 0 : -1;
        }
    }

    // ---------------------------------------------------------------- lifecycle

    async Task InitializeAsync()
    {
        App.LogDebug("OpenCode: initialization started.");
        await DetectServiceUrlAsync(force: true);

        if (string.IsNullOrEmpty(_token))
        {
            await PairAsync();
        }

        await RefreshAsync();
        StartEventStream();
    }

    void StartEventStream()
    {
        if (_streamCancellation != null) return;
        _streamCancellation = new CancellationTokenSource();
        CancellationToken token = _streamCancellation.Token;
        _ = Task.Run(() => EventStreamLoopAsync(token));
    }

    public void Dispose()
    {
        try { _pollTimer.Stop(); } catch { }
        try
        {
            _streamCancellation?.Cancel();
            _streamCancellation?.Dispose();
        }
        catch { }
        _streamCancellation = null;
        try { _http.Dispose(); } catch { }
        try { _streamHttp.Dispose(); } catch { }
    }

    // ---------------------------------------------------------------- settings

    static string? FindCliPath()
    {
        List<string> candidates = [];

        string cliRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ai.opencode.desktop", "cli");

        try
        {
            if (Directory.Exists(cliRoot))
            {
                candidates.AddRange(Directory.GetDirectories(cliRoot)
                    .OrderByDescending(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                    .Select(path => Path.Combine(path, "opencode-cli.exe")));
            }
        }
        catch
        {
            // Игнорируем проблемы доступа к каталогу.
        }

        candidates.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "@opencode-aidesktop", "resources", "opencode-cli.exe"));

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathVariable))
        {
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                candidates.Add(Path.Combine(directory.Trim(), "opencode-cli.exe"));
                candidates.Add(Path.Combine(directory.Trim(), "opencode.exe"));
            }
        }

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // Некорректный путь — просто пропускаем.
            }
        }

        return null;
    }

    static async Task<string> RunCliAsync(string cli, string arguments)
    {
        try
        {
            return await Task.Run(() =>
            {
                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process == null) return "";

                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> error = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(CliTimeoutMs))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                }

                return $"{output.GetAwaiter().GetResult()}\n{error.GetAwaiter().GetResult()}";
            });
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: CLI '{arguments}' failed: {ex.Message}");
            return "";
        }
    }

    async Task DetectServiceUrlAsync(bool force)
    {
        if (!force && DateTime.UtcNow.Ticks - _lastDiscoveryAt < TimeSpan.FromSeconds(15).Ticks)
        {
            return;
        }
        _lastDiscoveryAt = DateTime.UtcNow.Ticks;

        string? cli = await Task.Run(FindCliPath);
        string? discovered = null;

        if (cli != null)
        {
            string output = await RunCliAsync(cli, "service status");
            Match match = Regex.Match(output, @"https?://[^\s]+");
            if (match.Success)
            {
                string candidate = match.Value.TrimEnd('/');
                // Токен авторизации отправляется по этому адресу — принимаем только loopback.
                if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && uri.IsLoopback)
                {
                    discovered = candidate;
                }
                else
                {
                    App.LogDebug("OpenCode: ignoring non-loopback discovered URL.");
                }
            }
        }

        List<string> candidates = [];
        if (!string.IsNullOrEmpty(discovered)) candidates.Add(discovered);
        if (!candidates.Contains(_baseUrl)) candidates.Add(_baseUrl);
        if (!candidates.Contains(DefaultBaseUrl)) candidates.Add(DefaultBaseUrl);

        foreach (string candidate in candidates)
        {
            if (await ProbeAsync(candidate))
            {
                if (_baseUrl != candidate)
                {
                    App.LogDebug($"OpenCode: service discovered at {candidate}.");
                    _baseUrl = candidate;
                }
                return;
            }
        }

        App.LogDebug("OpenCode: service not found.");
    }

    async Task<bool> ProbeAsync(string baseUrl)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/api/info");
            using var response = await _http.SendAsync(request);
            int code = (int)response.StatusCode;
            return code is >= 200 and < 500;
        }
        catch
        {
            return false;
        }
    }

    async Task<bool> PairAsync()
    {
        string? cli = await Task.Run(FindCliPath);
        if (cli == null) return false;

        try
        {
            string output = await RunCliAsync(cli, "pair");
            Match match = Regex.Match(output, @"https?://[^\s/]+/auth/connect/[\w\-]+");
            if (!match.Success)
            {
                App.LogDebug("OpenCode: pair link was not found.");
                return false;
            }

            using var pairClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string json = await pairClient.GetStringAsync(match.Value);
            using JsonDocument document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("token", out JsonElement tokenElement)) return false;

            string? token = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(token)) return false;

            _token = token;
            SaveToken(token);
            App.LogDebug("OpenCode: paired successfully.");
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: pairing failed: {ex.Message}");
            return false;
        }
    }

    // ---------------------------------------------------------------- token

    void LoadCachedToken()
    {
        try
        {
            if (File.Exists(TokenFile))
            {
                byte[] protectedBytes = File.ReadAllBytes(TokenFile);
                byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, TokenEntropy, DataProtectionScope.CurrentUser);
                _token = Encoding.UTF8.GetString(plainBytes).Trim();
                return;
            }

            if (File.Exists(LegacyTokenFile))
            {
                string legacy = File.ReadAllText(LegacyTokenFile).Trim();
                if (legacy.Length > 0)
                {
                    _token = legacy;
                    SaveToken(legacy);
                    try { File.Delete(LegacyTokenFile); } catch { }
                    App.LogDebug("OpenCode: legacy token migrated to DPAPI storage.");
                }
            }
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: token read failed: {ex.Message}");
        }
    }

    static void SaveToken(string token)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            byte[] protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token), TokenEntropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(TokenFile, protectedBytes);
        }
        catch (Exception ex)
        {
            App.LogDebug($"OpenCode: token save failed: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- HTTP

    void ApplyAuthorization(HttpRequestMessage request)
    {
        if (string.IsNullOrEmpty(_token)) return;
        string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{_token}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    async Task<(int Status, string Body)> SendAsync(
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

            if (status == (int)HttpStatusCode.Unauthorized && allowRepair)
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

    static HashSet<string> ParseRunningSessions(JsonElement active)
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

    static List<OpenCodeSessionInfo> ParseSessions(JsonElement data, HashSet<string> running)
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

    static List<OpenCodeModelInfo> ParseModels(JsonElement data)
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

    static string DescribeModel(string modelId, string providerId, IReadOnlyList<OpenCodeModelInfo> models)
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

    // ---------------------------------------------------------------- commands

    public async Task<bool> SendPromptAsync(string text, string? persona = null)
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

        string finalPrompt = string.IsNullOrWhiteSpace(persona)
            ? prompt
            : $"{BuildPersonaInstruction(persona)}\n\nЗадача:\n{prompt}";

        string payload = JsonSerializer.Serialize(new { text = finalPrompt });
        (int status, string body) = await SendAsync(
            HttpMethod.Post, $"/api/session/{Esc(sessionId)}/prompt", payload);

        if (status is >= 200 and < 300)
        {
            lock (_gate)
            {
                _streamText.Clear();
                _lastAssistantText = "";
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
                _sessionTitle = GetString(data, "title");
                _sessionDirectory = targetDirectory;
                _isBusy = false;
                _pendingPermission = null;
                _streamText.Clear();
                _lastAssistantText = "";
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
            _sessionTitle = selected.Title;
            _sessionDirectory = selected.Directory;
            _sessionModelId = selected.ModelId;
            _sessionModelProviderId = selected.ModelProviderId;
            _isBusy = selected.IsRunning;
            _pendingPermission = null;
            _streamText.Clear();
            _lastAssistantText = "";
            _lastError = "";
            _needMessages = true;
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
        return status is >= 200 and < 300;
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

    void HandleEvent(string json)
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
                        _lastError = DescribeEventError(data);
                        _taskStartedAtUtc = null;
                        UpdateStatusText();
                    }
                    Notify(force: true);
                    _ = RefreshAsync();
                    break;

                case "session.text.started":
                    if (!selected) break;
                    lock (_gate) _streamText.Clear();
                    break;

                case "session.text.delta":
                {
                    if (!selected) break;
                    string delta = GetString(data, "delta");
                    if (delta.Length == 0) break;
                    lock (_gate)
                    {
                        if (_streamText.Length > 12000) _streamText.Remove(0, _streamText.Length - 12000);
                        _streamText.Append(delta);
                        _lastAssistantText = _streamText.ToString();
                    }
                    Notify();
                    break;
                }

                case "session.text.ended":
                    if (!selected) break;
                    lock (_gate)
                    {
                        string text = _streamText.ToString().Trim();
                        if (text.Length > 0) _lastAssistantText = text;
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

    static string GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return "";
        if (!element.TryGetProperty(property, out JsonElement value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    }

    static string? GetStringOrNull(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static string Esc(string value) => Uri.EscapeDataString(value);

    static string BuildPersonaInstruction(string persona)
    {
        const int maxExcerpt = 1600;
        string? relative = persona switch
        {
            "Desktop App Engineer" => @"engineering\engineering-desktop-app-engineer.md",
            "Frontend Developer" => @"engineering\engineering-frontend-developer.md",
            "Code Reviewer" => @"engineering\engineering-code-reviewer.md",
            "Bug Fixer" => @"engineering\engineering-minimal-change-engineer.md",
            _ => null
        };

        string instruction = $"Действуй как специалист по роли «{persona}».";
        if (relative == null) return instruction;

        string? file = FindAgencyFile(relative);
        if (file == null) return instruction;

        try
        {
            string content = File.ReadAllText(file).Trim();
            if (content.Length > maxExcerpt) content = content[..maxExcerpt] + "\n...";
            return $"Действуй строго по инструкции роли «{persona}»:\n\n{content}";
        }
        catch
        {
            return instruction;
        }
    }

    static string? FindAgencyFile(string relativePath)
    {
        string current = AppContext.BaseDirectory;
        for (int depth = 0; depth < 6; depth++)
        {
            string candidate = Path.Combine(current, "tools", "agency-agents", relativePath);
            if (File.Exists(candidate)) return candidate;

            DirectoryInfo? parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }

    static string DescribeFailure(string prefix, int status, string body)
    {
        string detail = status switch
        {
            0 => "нет связи с OpenCode",
            401 => "требуется авторизация",
            404 => "сессия не найдена",
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

    static string Shorten(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";
}

sealed class OpenCodeApiException(int status, string body) : Exception($"OpenCode API {status}")
{
    public int Status { get; } = status;
    public string Body { get; } = body;
}
