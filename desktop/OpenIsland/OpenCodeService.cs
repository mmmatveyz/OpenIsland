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

public sealed record OpenCodeMessage(string Role, string Text)
{
    public string Label => Role == "user" ? "Вы" : "OpenCode";

    /// <summary>Текст без markdown-разметки — для обычного <c>TextBlock</c>.</summary>
    public string DisplayText => MarkdownText.ToPlainText(Text);
}

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

    readonly HttpClient _http;
    readonly HttpClient _streamHttp;
    readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromMilliseconds(PollingIntervalMs) };

    readonly object _gate = new();
    readonly StringBuilder _streamText = new();

    List<OpenCodeSessionInfo> _sessions = [];
    List<OpenCodeModelInfo> _models = [];
    List<OpenCodeMessage> _messages = [];

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
    bool _streamingMessageActive;
    bool _messagesLoaded;
    readonly bool _enablePairing = true;
    int _refreshing;
    int _syncQueued;
    int _eventsReceived;
    long _messagesRevision;
    long _executionsStarted;
    long _messagesLoadGeneration;
    long _lastNotifyAt;
    long _lastDiscoveryAt;
    DateTime? _taskStartedAtUtc;
    CancellationTokenSource? _streamCancellation;

    public OpenCodeService()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        LoadCachedToken();
        _pollTimer.Tick += async (_, _) => await RefreshAsync();
        _pollTimer.Start();
        _ = Task.Run(InitializeAsync);
    }

    /// <summary>
    /// Только для тестов: свой HTTP-транспорт, без фоновых циклов и пейринга,
    /// адрес демона задаётся напрямую.
    /// </summary>
    internal OpenCodeService(HttpMessageHandler handler, string baseUrl)
    {
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(10) };
        _streamHttp = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _baseUrl = baseUrl;
        _isConnected = true;
        _enablePairing = false;
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
    public IReadOnlyList<OpenCodeMessage> Messages
    {
        get
        {
            lock (_gate)
            {
                return [.. _messages];
            }
        }
    }
    public long MessagesRevision => Interlocked.Read(ref _messagesRevision);

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

}

sealed class OpenCodeApiException(int status, string body) : Exception($"OpenCode API {status}")
{
    public int Status { get; } = status;
    public string Body { get; } = body;
}
