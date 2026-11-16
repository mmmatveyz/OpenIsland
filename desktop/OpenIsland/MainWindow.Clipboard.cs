using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenIsland;

/// <summary>
/// История буфера обмена: захват идёт из сообщения <c>WM_CLIPBOARDUPDATE</c>
/// (хук вешается в <c>OnSourceInitialized</c>), карточка показывает последние
/// фрагменты и по клику возвращает выбранный обратно в буфер.
/// </summary>
public partial class MainWindow
{
    const int ClipboardPreviewLength = 80;

    readonly ClipboardService _clipboard = new();
    bool _clipboardReady;

    void InitClipboard()
    {
        _clipboard.Changed += OnClipboardChanged;
        _clipboardReady = true;
        RefreshClipboardUI();
    }

    void OnClipboardChanged()
    {
        if (!_clipboardReady) return;
        try
        {
            Dispatcher.BeginInvoke(RefreshClipboardUI);
        }
        catch
        {
            // Окно уже закрывается.
        }
    }

    /// <summary>Хук окна: буфер обмена изменился (сообщение приходит только после подписки в Native.WatchClipboard).</summary>
    IntPtr OnClipboardMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != Native.WM_CLIPBOARDUPDATE) return IntPtr.Zero;
        handled = true;
        _clipboard.Capture();
        return IntPtr.Zero;
    }

    void ClipboardRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Clipboard);

    void ClipboardBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    void ClipboardClear_Click(object sender, RoutedEventArgs e)
    {
        _clipboard.Clear();
        RefreshClipboardUI();
    }

    void ClipboardItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int index }) return;
        IReadOnlyList<ClipboardService.Entry> items = _clipboard.Items;
        if (index < 0 || index >= items.Count) return;
        _clipboard.Copy(items[index].Text);
        RefreshClipboardUI();
    }

    void RefreshClipboardUI()
    {
        if (!_clipboardReady) return;

        bool recording = Settings.Clipboard;
        IReadOnlyList<ClipboardService.Entry> items = _clipboard.Items;

        if (MenuClipboardCount != null)
        {
            MenuClipboardCount.Text = recording
                ? (items.Count == 0 ? "" : items.Count.ToString())
                : "выкл.";
        }

        // Список строк перестраиваем только когда карточка буфера реально открыта —
        // иначе это лишняя работа на каждое копирование.
        if (_view != View.Clipboard) return;

        ClipboardClear.IsEnabled = items.Count > 0;
        ClipboardEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipboardEmpty.Text = recording
            ? "Скопируйте что-нибудь — история появится здесь"
            : "История выключена. Включите её в настройках.";

        ClipboardList.Children.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            ClipboardList.Children.Add(CreateClipboardRow(items[i], i));
        }

        UpdateTargets();
    }

    Button CreateClipboardRow(ClipboardService.Entry entry, int index)
    {
        var preview = new TextBlock
        {
            Text = Preview(entry.Text),
            FontSize = 12.5,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var age = new TextBlock
        {
            Text = Relative(entry.AtUtc),
            FontSize = 10.5,
            Foreground = _dim,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(preview, 0);
        Grid.SetColumn(age, 1);
        grid.Children.Add(preview);
        grid.Children.Add(age);

        var button = new Button
        {
            Style = (Style)FindResource("RowButton"),
            Content = grid,
            Tag = index,
            ToolTip = entry.Text.Length > 400 ? entry.Text[..400] + "…" : entry.Text,
        };
        button.Click += ClipboardItem_Click;
        return button;
    }

    static string Preview(string text)
    {
        string flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= ClipboardPreviewLength ? flat : flat[..(ClipboardPreviewLength - 1)] + "…";
    }

    static string Relative(DateTime utc)
    {
        TimeSpan age = DateTime.UtcNow - utc;
        if (age.TotalSeconds < 45) return "сейчас";
        if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes} мин";
        if (age.TotalHours < 24) return $"{(int)age.TotalHours} ч";
        return $"{(int)age.TotalDays} д";
    }
}
