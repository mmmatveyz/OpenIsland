using System.Runtime.InteropServices;
using System.Text;

namespace OpenIsland;

/// <summary>
/// Управление Winamp-совместимыми плеерами (Winamp, WACUP, AIMP) через
/// классический IPC-протокол окна <c>Winamp v1.x</c>.
/// <para>
/// Читаем только скалярные <c>WM_USER</c>-селекторы и заголовок окна:
/// селекторы, возвращающие указатели (<c>IPC_GETPLAYLISTTITLE</c> и т. п.),
/// нельзя разыменовать в чужом процессе. Все вызовы идут через
/// <c>SendMessageTimeout</c>, чтобы зависший плеер не подвесил UI.
/// </para>
/// </summary>
sealed class WinampIpc
{
    public enum State
    {
        Stopped = 0,
        Playing = 1,
        Paused = 3,
    }

    public readonly record struct Reading(
        State State,
        string Artist,
        string Title,
        TimeSpan Position,
        TimeSpan Duration,
        string Executable);

    const string WindowClass = "Winamp v1.x";
    const uint WM_COMMAND = 0x0111, WM_USER = 0x0400;
    const uint SMTO_BLOCK = 0x0001, SMTO_ABORTIFHUNG = 0x0002;
    const uint TimeoutMs = 120;
    const int TitleCapacity = 512;
    const int ClassNameCapacity = 64;

    internal const int IsPlayingSelector = 104;
    internal const int OutputTimeSelector = 105;
    internal const int JumpToTimeSelector = 106;

    internal const int PreviousCommand = 40044;
    internal const int PlayCommand = 40045;
    internal const int PauseCommand = 40046;
    internal const int NextCommand = 40048;

    readonly object _gate = new();
    IntPtr _window;
    uint _process;
    string _executable = "";

    delegate bool WindowProc(IntPtr window, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(WindowProc callback, IntPtr param);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetWindowText(IntPtr window, StringBuilder text, int max);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SendMessageTimeout(
        IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    public Reading? TryRead()
    {
        lock (_gate)
        {
            if (!EnsureWindow()) return null;
            try
            {
                if (!Send(IsPlayingSelector, IntPtr.Zero, out IntPtr status)) return null;
                State state = StateOf(status.ToInt64());

                if (!Send(OutputTimeSelector, new IntPtr(1), out IntPtr seconds)) return null;
                TimeSpan duration = DurationFromSeconds(seconds.ToInt64());

                TimeSpan position = TimeSpan.Zero;
                if (state == State.Playing)
                {
                    if (!Send(OutputTimeSelector, IntPtr.Zero, out IntPtr milliseconds)) return null;
                    position = PositionFromMilliseconds(milliseconds.ToInt64());
                }

                (string artist, string title) = WinampTitle.Parse(WindowText());
                if (title.Length == 0 && SourceApp.WindowTitle(_process) is { Length: > 0 } fallback)
                    (artist, title) = WinampTitle.Parse(fallback);
                return new Reading(state, artist, title, position, duration, _executable);
            }
            catch (Exception ex)
            {
                App.LogDebug($"Winamp IPC: чтение не удалось ({_executable}): {ex.Message}");
                ResetWindow();
                return null;
            }
        }
    }

    public void TogglePlay(bool playing)
    {
        if (playing) Command(PauseCommand);
        else Command(PlayCommand);
    }

    public void Previous() => Command(PreviousCommand);

    public void Next() => Command(NextCommand);

    public bool Seek(TimeSpan position)
    {
        lock (_gate)
        {
            if (!EnsureWindow()) return false;
            long milliseconds = (long)Math.Clamp(position.TotalMilliseconds, 0, int.MaxValue);
            if (SendMessageTimeout(_window, WM_USER, new IntPtr(milliseconds), new IntPtr(JumpToTimeSelector),
                    SMTO_BLOCK | SMTO_ABORTIFHUNG, TimeoutMs, out IntPtr result) == IntPtr.Zero)
                return false;
            return result.ToInt64() != -1;
        }
    }

    void Command(int id)
    {
        lock (_gate)
        {
            if (!EnsureWindow()) return;
            PostMessage(_window, WM_COMMAND, new IntPtr(id), IntPtr.Zero);
        }
    }

    bool Send(int selector, IntPtr data, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (_window == IntPtr.Zero) return false;
        if (SendTo(_window, selector, data, out result)) return true;

        // Плеер закрылся или перестал отвечать — найдём окно заново при следующем опросе.
        ResetWindow();
        return false;
    }

    static bool SendTo(IntPtr window, int selector, IntPtr data, out IntPtr result) =>
        SendMessageTimeout(window, WM_USER, data, new IntPtr(selector),
            SMTO_BLOCK | SMTO_ABORTIFHUNG, TimeoutMs, out result) != IntPtr.Zero;

    /// <summary>
    /// Выбирает окно плеера: сначала тот, что реально играет, затем приостановленный,
    /// иначе любой найденный. Так при одновременно запущенных Winamp и AIMP
    /// показывается активный источник.
    /// </summary>
    bool EnsureWindow()
    {
        bool valid = _window != IntPtr.Zero && IsWindow(_window);
        if (valid && ReadState(_window) == State.Playing) return true;

        // Текущий плеер не играет — ищем лучший среди всех окон.
        if (TryAttachBest()) return true;
        if (!valid) ResetWindow();
        return valid;
    }

    bool TryAttachBest()
    {
        IntPtr best = IntPtr.Zero;
        int bestRank = -1;

        EnumWindows((window, _) =>
        {
            if (!IsPlayerWindow(window)) return true;
            int rank = RankOf(ReadState(window));
            if (rank > bestRank)
            {
                bestRank = rank;
                best = window;
            }
            return true;
        }, IntPtr.Zero);

        if (best == IntPtr.Zero) return false;
        Attach(best);
        return true;
    }

    static int RankOf(State? state) => state switch
    {
        State.Playing => 2,
        State.Paused => 1,
        State.Stopped => 0,
        _ => -1, // окно не ответило
    };

    static bool IsPlayerWindow(IntPtr window)
    {
        if (!IsWindowVisible(window)) return false;
        var text = new StringBuilder(ClassNameCapacity);
        return GetClassName(window, text, text.Capacity) > 0
            && text.ToString().Equals(WindowClass, StringComparison.OrdinalIgnoreCase);
    }

    State? ReadState(IntPtr window) =>
        SendTo(window, IsPlayingSelector, IntPtr.Zero, out IntPtr status)
            ? StateOf(status.ToInt64())
            : null;

    void Attach(IntPtr window)
    {
        _window = window;
        GetWindowThreadProcessId(window, out _process);
        _executable = SourceApp.ExecutableName(_process);
    }

    void ResetWindow()
    {
        _window = IntPtr.Zero;
        _process = 0;
        _executable = "";
    }

    string WindowText()
    {
        var text = new StringBuilder(TitleCapacity);
        return GetWindowText(_window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

    internal static State StateOf(long code) => code switch
    {
        1 => State.Playing,
        3 => State.Paused,
        _ => State.Stopped,
    };

    internal static TimeSpan PositionFromMilliseconds(long milliseconds) =>
        milliseconds < 0 || milliseconds > TimeSpan.MaxValue.TotalMilliseconds
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(milliseconds);

    internal static TimeSpan DurationFromSeconds(long seconds) =>
        seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(seconds);
}

/// <summary>
/// Разбор заголовка Winamp-совместимого плеера: убирает маркеры состояния,
/// суффикс плеера и номер трека, затем делит «Artist - Title».
/// Чистая логика без Win32 — покрывается тестами.
/// </summary>
static class WinampTitle
{
    static readonly string[] Markers = ["[Paused]", "[Stopped]", "[Playing]"];

    static readonly string[] Players =
        ["Winamp", "WACUP", "AIMP", "AIMP2", "AIMP3", "AIMP4", "Apollo", "XMPlay", "MusicBee"];

    public static (string Artist, string Title) Parse(string? raw)
    {
        string text = (raw ?? "").Trim();
        text = StripMarker(text);
        if (text.Length == 0) return ("", "");

        // Заголовок вроде «AIMP» или «Winamp 5.666» без трека.
        if (IsPlayerSuffix(text)) return ("", "");

        text = StripPlayer(text);
        text = StripNumber(text).Trim();
        if (text.Length == 0) return ("", "");

        int dash = text.IndexOf(" - ", StringComparison.Ordinal);
        if (dash < 0) return ("", text);

        string artist = text[..dash].Trim();
        string title = text[(dash + 3)..].Trim();
        return title.Length == 0 ? ("", artist) : (artist, title);
    }

    internal static bool IsPlayerSuffix(string value)
    {
        foreach (string player in Players)
        {
            if (value.Equals(player, StringComparison.OrdinalIgnoreCase)) return true;
            if (value.StartsWith(player + " ", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.StartsWith(player + " v", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    static string StripMarker(string text)
    {
        foreach (string marker in Markers)
            if (text.EndsWith(marker, StringComparison.OrdinalIgnoreCase))
                return text[..^marker.Length].TrimEnd();
        return text;
    }

    static string StripPlayer(string text)
    {
        int dash = text.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dash < 0) return text;
        return IsPlayerSuffix(text[(dash + 3)..].Trim()) ? text[..dash].TrimEnd() : text;
    }

    static string StripNumber(string text)
    {
        int digits = 0;
        while (digits < text.Length && char.IsAsciiDigit(text[digits])) digits++;
        if (digits == 0 || digits + 1 >= text.Length || text[digits] != '.' || text[digits + 1] != ' ')
            return text;
        return text[(digits + 2)..];
    }
}
