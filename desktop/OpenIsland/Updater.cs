using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenIsland;

sealed class Updater
{
    public enum Stage { Idle, Checking, Latest, Available, Loading, Failed, NotConfigured }

    public const string RestartedFlag = "--updated";

    // Репозиторий наших релизов в формате "owner/repo". Основной канал — GitHub API
    // (/releases/latest): там есть браузерная ссылка на ассет и его sha256-дайджест.
    // Но анонимный API ограничен 60 запросами в час на IP, поэтому при отказе
    // (403/429) есть запасной канал — обычная страница релиза, которая редиректом
    // отдаёт тег последнего выпуска (без лимита API).
    const string Repository = "mmmatveyz/OpenIsland";
    const string AssetName = "OpenIsland.exe";
    const string ReleaseTagMarker = "/releases/tag/";
    static string LatestReleaseUrl => Repository.Length > 0
        ? $"https://api.github.com/repos/{Repository}/releases/latest" : "";
    static string LatestPageUrl => Repository.Length > 0
        ? $"https://github.com/{Repository}/releases/latest" : "";
    const string HashPrefix = "sha256:";
    const int MaxNotes = 6;
    const int BufferSize = 1 << 16;
    const int CleanUpAttempts = 10;
    static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    static readonly TimeSpan FailureRetry = TimeSpan.FromMinutes(10);
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CleanUpRetry = TimeSpan.FromSeconds(1);
    static readonly HttpClient Http;
    static readonly HttpClient PlainHttp;

    static Updater()
    {
        CurrentVersion = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        Http = CreateClient(CurrentVersion);
        PlainHttp = CreateClient(CurrentVersion, allowRedirects: false);
    }

    readonly HttpClient _http, _plainHttp;
    string _downloadUrl = "", _digest = "";
    DateTime _checkedAt;

    public Updater() : this(null, null) { }

    internal Updater(HttpClient? http, HttpClient? plainHttp = null)
    {
        _http = http ?? Http;
        _plainHttp = plainHttp ?? PlainHttp;
    }

    public static Version CurrentVersion { get; }

    public static bool IsConfigured => Repository.Length > 0;

    public event Action? Changed;

    public Stage State { get; private set; }

    public Version? LatestVersion { get; private set; }

    public string[] Notes { get; private set; } = [];

    /// <summary>GitHub ответил 403/429: анонимный лимит API исчерпан, использован запасной канал.</summary>
    public bool RateLimited { get; private set; }

    public int Percent { get; private set; }

    public long DownloadedBytes { get; private set; }

    public long TotalBytes { get; private set; }

    static string ExePath => Environment.ProcessPath ?? "";
    static string OldPath => ExePath + ".old";
    static string DownloadPath => ExePath + ".new";

    public async Task CheckAsync(bool force = false)
    {
        if (!IsConfigured)
        {
            SetState(Stage.NotConfigured);
            return;
        }

        if (State is Stage.Checking or Stage.Loading) return;

        // Неудачу тоже запоминаем: иначе каждый заход в «Настройки»/«Обновление» повторял бы
        // запрос и выжигал лимит GitHub. Кнопка «Попробовать ещё раз» приходит с force
        // и обходит паузу.
        TimeSpan interval = State == Stage.Failed ? FailureRetry : CheckInterval;
        if (!force && DateTime.UtcNow - _checkedAt < interval) return;

        SetState(Stage.Checking);
        RateLimited = false;

        bool loaded = await TryLoadFromApiAsync();
        if (!loaded) loaded = await TryLoadFromReleasePageAsync();

        _checkedAt = DateTime.UtcNow;
        if (!loaded)
        {
            SetState(Stage.Failed);
            return;
        }

        SetState(LatestVersion > CurrentVersion && _downloadUrl.Length > 0 ? Stage.Available : Stage.Latest);
    }

    async Task<bool> TryLoadFromApiAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            using var document = JsonDocument.Parse(await _http.GetStringAsync(LatestReleaseUrl, timeout.Token));
            ApplyApiRelease(document.RootElement);
            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
        {
            RateLimited = true;
            App.LogDebug($"Updater: GitHub API rate limit ({ex.StatusCode}), using the release page.");
            return false;
        }
        catch (Exception ex)
        {
            App.LogDebug($"Updater: GitHub API check failed: {ex.Message}");
            return false;
        }
    }

    void ApplyApiRelease(JsonElement release)
    {
        LatestVersion = Normalize(Version.Parse(release.GetProperty("tag_name").GetString()!.TrimStart('v')));
        Notes = release.TryGetProperty("body", out JsonElement body) ? ParseNotes(body.GetString() ?? "") : [];
        _downloadUrl = _digest = "";
        TotalBytes = 0;
        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != AssetName) continue;
            _downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (asset.TryGetProperty("digest", out JsonElement digest)) _digest = digest.GetString() ?? "";
            if (asset.TryGetProperty("size", out JsonElement size)) TotalBytes = size.GetInt64();
        }
    }

    /// <summary>
    /// Запасной канал без GitHub API: страница /releases/latest отвечает редиректом на
    /// /releases/tag/&lt;тег&gt;, откуда берётся версия и прямой адрес файла. Дайджест в этом
    /// случае недоступен, но файл всё так же отдаётся по HTTPS с github.com.
    /// </summary>
    async Task<bool> TryLoadFromReleasePageAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            using var response = await _plainHttp.GetAsync(LatestPageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

            string? tag = ExtractTag(response.Headers.Location?.OriginalString ?? "");
            if (tag == null) return false;

            LatestVersion = Normalize(Version.Parse(tag.TrimStart('v')));
            Notes = [];
            _digest = "";
            TotalBytes = 0;
            _downloadUrl = AssetUrl(tag);
            return true;
        }
        catch (Exception ex)
        {
            App.LogDebug($"Updater: release page check failed: {ex.Message}");
            return false;
        }
    }

    internal static string AssetUrl(string tag) =>
        $"https://github.com/{Repository}/releases/download/{tag}/{AssetName}";

    /// <summary>Достаёт тег из адреса вида https://github.com/owner/repo/releases/tag/v1.2.3.</summary>
    internal static string? ExtractTag(string url)
    {
        int index = url.IndexOf(ReleaseTagMarker, StringComparison.Ordinal);
        if (index < 0) return null;

        string tag = url[(index + ReleaseTagMarker.Length)..];
        int cut = tag.IndexOfAny(new[] { '?', '#', '/' });
        if (cut >= 0) tag = tag[..cut];
        tag = tag.Trim();
        return tag.Length > 0 ? tag : null;
    }

    public async Task<bool> InstallAsync()
    {
        if (State != Stage.Available) return false;

        Percent = 0;
        DownloadedBytes = 0;
        SetState(Stage.Loading);
        try
        {
            await DownloadAsync();
            ReplaceExe();
            Process.Start(new ProcessStartInfo(ExePath, RestartedFlag) { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            try { File.Delete(DownloadPath); }
            catch { }
            SetState(Stage.Failed);
            return false;
        }
    }

    async Task DownloadAsync()
    {
        using var stalled = new CancellationTokenSource(StallTimeout);
        using HttpResponseMessage response = await _http.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead, stalled.Token);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0, done = 0;
        if (total > 0) TotalBytes = total;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (Stream from = await response.Content.ReadAsStreamAsync(stalled.Token))
        await using (var to = new FileStream(DownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await from.ReadAsync(buffer, stalled.Token)) > 0)
            {
                stalled.CancelAfter(StallTimeout);
                await to.WriteAsync(buffer.AsMemory(0, read));
                sha.AppendData(buffer, 0, read);
                done += read;
                int percent = total > 0 ? (int)(done * 100 / total) : 0;
                if (percent == Percent) continue;
                Percent = percent;
                DownloadedBytes = done;
                Changed?.Invoke();
            }
        }

        if (total > 0 && done != total) throw new IOException($"The update came short: {done} of {total} bytes.");
        string hash = HashPrefix + Convert.ToHexString(sha.GetHashAndReset());
        if (_digest.Length > 0 && !hash.Equals(_digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update does not match the hash GitHub gives for it.");
    }

    static void ReplaceExe()
    {
        File.Move(ExePath, OldPath, true);
        try
        {
            File.Move(DownloadPath, ExePath);
        }
        catch
        {
            File.Move(OldPath, ExePath);
            throw;
        }
    }

    public static void CleanUp() => Task.Run(async () =>
    {
        for (int attempt = 0; attempt < CleanUpAttempts && File.Exists(OldPath); attempt++)
        {
            try { File.Delete(OldPath); }
            catch { await Task.Delay(CleanUpRetry); }
        }
    });

    void SetState(Stage stage)
    {
        State = stage;
        Changed?.Invoke();
    }

    internal static string[] ParseNotes(string notes) => notes.Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 2 && line[0] is '-' or '*' or '•' && line[1] == ' ')
        .Select(line => line[2..].Replace("**", "").Replace("`", "").Trim())
        .Where(line => line.Length > 0 && !line.Contains("http", StringComparison.OrdinalIgnoreCase))
        .Take(MaxNotes)
        .ToArray();

    internal static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    static HttpClient CreateClient(Version current, bool allowRedirects = true)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = allowRedirects };
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenIsland/" + current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
