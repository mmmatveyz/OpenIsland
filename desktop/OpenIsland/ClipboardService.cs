using System.Windows;

namespace OpenIsland;

/// <summary>
/// История буфера обмена. Живёт только в памяти процесса: ничего не пишется
/// на диск и не улетает в логи или сеть. Захват включается подпиской окна на
/// <c>WM_CLIPBOARDUPDATE</c> (см. <see cref="Native.WatchClipboard"/>), поэтому
/// история копится, даже когда карточка закрыта.
/// </summary>
sealed class ClipboardService
{
    const int MaxItems = 30;
    const int MaxStoredChars = 20000;
    const int Attempts = 3;
    const int RetryDelayMs = 10;

    readonly List<Entry> _items = [];

    public readonly record struct Entry(string Text, DateTime AtUtc);

    public event Action? Changed;

    public IReadOnlyList<Entry> Items => _items;

    public bool Recording => Settings.Clipboard;

    /// <summary>Читает текущий буфер и добавляет фрагмент в начало истории.</summary>
    public void Capture()
    {
        if (!Recording) return;

        string? text = ReadClipboard();
        if (text is not null) Add(text);
    }

    /// <summary>
    /// Кладёт фрагмент в начало истории: пустое не берём, дубликат поднимаем наверх,
    /// длинный обрезаем, а всё, что вышло за лимит, отбрасываем.
    /// Отдельно от <see cref="Capture"/> — чтобы логику истории можно было проверить тестами.
    /// </summary>
    internal void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.Length > MaxStoredChars) text = text[..MaxStoredChars];
        if (_items.Count > 0 && _items[0].Text == text) return;

        _items.RemoveAll(entry => entry.Text == text);
        _items.Insert(0, new Entry(text, DateTime.UtcNow));
        if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
        Changed?.Invoke();
    }

    /// <summary>
    /// Читает текст из буфера. Буфер бывает кратко занят другим процессом, поэтому
    /// пробуем несколько раз, прежде чем сдаться (каждый пропуск — потерянный фрагмент).
    /// </summary>
    static string? ReadClipboard()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                if (!Clipboard.ContainsText()) return null;
                string text = Clipboard.GetText();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception ex)
            {
                if (attempt >= Attempts)
                {
                    App.LogDebug($"Clipboard: capture skipped ({ex.Message}).");
                    return null;
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    public void Copy(string text)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= Attempts)
                {
                    App.LogDebug($"Clipboard: copy failed ({ex.Message}).");
                    return;
                }
                Thread.Sleep(RetryDelayMs);
            }
        }
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }
}
