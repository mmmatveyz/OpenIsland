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

    readonly List<Entry> _items = [];

    public readonly record struct Entry(string Text, DateTime AtUtc);

    public event Action? Changed;

    public IReadOnlyList<Entry> Items => _items;

    public bool Recording => Settings.Clipboard;

    /// <summary>Читает текущий буфер и добавляет фрагмент в начало истории.</summary>
    public void Capture()
    {
        if (!Recording) return;

        try
        {
            if (!Clipboard.ContainsText()) return;

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text.Length > MaxStoredChars) text = text[..MaxStoredChars];
            if (_items.Count > 0 && _items[0].Text == text) return;

            _items.RemoveAll(entry => entry.Text == text);
            _items.Insert(0, new Entry(text, DateTime.UtcNow));
            if (_items.Count > MaxItems) _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            // Буфер может быть занят другим процессом — это нормально, просто пропускаем кадр.
            App.LogDebug($"Clipboard: capture skipped ({ex.Message}).");
        }
    }

    public void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            App.LogDebug($"Clipboard: copy failed ({ex.Message}).");
        }
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke();
    }
}
