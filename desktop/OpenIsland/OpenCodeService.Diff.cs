using System.Text.Json;

namespace OpenIsland;

/// <summary>Изменённый агентом файл из <c>GET /api/session/{id}/diff</c>.</summary>
public sealed record OpenCodeFileDiff(
    string File,
    string Patch,
    int Additions,
    int Deletions,
    string Status)
{
    /// <summary>Имя файла без каталогов — для строки списка.</summary>
    public string Name
    {
        get
        {
            int slash = File.LastIndexOfAny(['/', '\\']);
            return slash >= 0 && slash < File.Length - 1 ? File[(slash + 1)..] : File;
        }
    }

    /// <summary>Каталог файла без имени — вторая строка списка.</summary>
    public string Folder
    {
        get
        {
            int slash = File.LastIndexOfAny(['/', '\\']);
            return slash > 0 ? File[..slash] : "";
        }
    }

    public string Stats => $"+{Additions} \u2212{Deletions}";

    public string StatusLabel => Status switch
    {
        "added" => "новый файл",
        "deleted" => "удалён",
        _ => "изменён",
    };
}

/// <summary>Строка патча: заголовок ханка, добавление, удаление или контекст.</summary>
public sealed record OpenCodeDiffLine(string Kind, string Text);

public sealed partial class OpenCodeService
{
    /// <summary>Сколько строк патча максимум отдаём в интерфейс — чтобы не строить тысячи элементов.</summary>
    const int MaxPatchLines = 300;
    const int MaxPatchLineLength = 240;

    public IReadOnlyList<OpenCodeFileDiff> Diffs => Volatile.Read(ref _diffs);
    public long DiffsRevision => Interlocked.Read(ref _diffRevision);
    public string DiffError => _diffError;

    public bool IsDiffLoading
    {
        get { lock (_gate) return _diffLoading; }
    }

    /// <summary>Загружает изменения выбранной сессии. Вызывается по требованию, не по таймеру.</summary>
    public async Task<bool> RefreshDiffAsync()
    {
        string? sessionId;
        lock (_gate) sessionId = _sessionId;
        if (sessionId == null) return false;

        long generation = Interlocked.Increment(ref _diffLoadGeneration);
        lock (_gate)
        {
            _diffLoading = true;
            _diffError = "";
        }
        Notify(force: true);

        try
        {
            JsonElement data = await GetDataAsync($"/api/session/{Esc(sessionId)}/diff");
            List<OpenCodeFileDiff> diffs = ParseDiffs(data);
            lock (_gate)
            {
                if (_sessionId != sessionId || generation != Interlocked.Read(ref _diffLoadGeneration)) return false;
                Volatile.Write(ref _diffs, diffs);
                _diffLoading = false;
            }
            Interlocked.Increment(ref _diffRevision);
            Notify(force: true);
            return true;
        }
        catch (Exception ex)
        {
            bool stale;
            lock (_gate)
            {
                stale = _sessionId != sessionId || generation != Interlocked.Read(ref _diffLoadGeneration);
                if (!stale)
                {
                    _diffLoading = false;
                    _diffError = ex is OpenCodeApiException api
                        ? DescribeFailure("Изменения", api.Status, api.Body)
                        : "Изменения: нет связи с OpenCode";
                }
            }

            if (!stale)
            {
                Interlocked.Increment(ref _diffRevision);
                Notify(force: true);
            }
            App.LogDebug($"OpenCode: diff load failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Сбрасывает диффы при смене или удалении сессии.</summary>
    void ResetDiffState()
    {
        Interlocked.Increment(ref _diffLoadGeneration);
        lock (_gate)
        {
            _diffLoading = false;
            _diffError = "";
        }
        Volatile.Write(ref _diffs, []);
        Interlocked.Increment(ref _diffRevision);
    }

    internal static List<OpenCodeFileDiff> ParseDiffs(JsonElement data)
    {
        List<OpenCodeFileDiff> diffs = [];
        if (data.ValueKind != JsonValueKind.Array) return diffs;

        foreach (JsonElement item in data.EnumerateArray())
        {
            string file = GetString(item, "file");
            if (string.IsNullOrEmpty(file)) continue;

            diffs.Add(new OpenCodeFileDiff(
                file,
                GetString(item, "patch"),
                GetInt(item, "additions"),
                GetInt(item, "deletions"),
                GetString(item, "status")));
        }

        return diffs;
    }

    /// <summary>Разбирает unified diff на строки с типом, отбрасывая заголовки файлов.</summary>
    internal static List<OpenCodeDiffLine> SplitPatch(string? patch)
    {
        List<OpenCodeDiffLine> lines = [];
        if (string.IsNullOrEmpty(patch)) return lines;

        string[] raw = patch.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        bool truncated = false;
        foreach (string source in raw)
        {
            if (lines.Count >= MaxPatchLines)
            {
                truncated = true;
                break;
            }

            if (IsFileHeader(source)) continue;

            string kind =
                source.StartsWith("@@", StringComparison.Ordinal) ? "hunk"
                : source.StartsWith('+') ? "added"
                : source.StartsWith('-') ? "removed"
                : "context";

            string text = source.Length > MaxPatchLineLength
                ? source[..MaxPatchLineLength] + "\u2026"
                : source;
            lines.Add(new OpenCodeDiffLine(kind, text));
        }

        if (truncated) lines.Add(new OpenCodeDiffLine("more", "\u2026 патч обрезан"));
        return lines;
    }

    /// <summary>Заголовки файлов unified diff ("+++ b/x" / "--- a/x"), но не строки содержимого.</summary>
    static bool IsFileHeader(string line) =>
        (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("+++\t", StringComparison.Ordinal))
        || (line.StartsWith("--- ", StringComparison.Ordinal) || line.StartsWith("---\t", StringComparison.Ordinal));

    static int GetInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty(property, out JsonElement value)) return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : 0;
    }
}
