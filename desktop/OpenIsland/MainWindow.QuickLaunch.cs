using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace OpenIsland;

public partial class MainWindow
{
    DelayedAction? _quickLaunchNotice;
    bool _quickLaunchTilesBuilt;
    bool _quickLaunchDialog;

    void InitQuickLaunch()
    {
        BuildQuickLaunchTiles();
        RefreshQuickLaunchOwn();
    }

    void QuickLaunchRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.QuickLaunch);

    void QuickLaunchBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    /// <summary>
    /// Плитки системных утилит строятся один раз: цели фиксированы, а состояние
    /// читается из реестра только для списка своих программ.
    /// </summary>
    void BuildQuickLaunchTiles()
    {
        if (_quickLaunchTilesBuilt) return;
        _quickLaunchTilesBuilt = true;

        foreach (QuickAction action in QuickActions.SystemActions())
        {
            var icon = new Icon
            {
                Kind = GlyphFor(action),
                Width = 14,
                Height = 14,
                Fill = _dim,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            var label = new TextBlock
            {
                Text = action.Name,
                FontSize = 12,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(icon);
            content.Children.Add(label);

            var tile = new Button
            {
                Style = (Style)FindResource("ChipButton"),
                Height = 40,
                Margin = new Thickness(2),
                Content = content,
                Tag = action,
                ToolTip = action.Target,
            };
            tile.Click += QuickLaunchTile_Click;
            QuickLaunchTiles.Children.Add(tile);
        }
    }

    static Glyph GlyphFor(QuickAction action) => action.Target switch
    {
        "explorer.exe" => Glyph.Folder,
        "wt.exe" or "powershell.exe" => Glyph.Bolt,
        "calc.exe" => Glyph.Plus,
        "notepad.exe" => Glyph.Lines,
        "taskmgr.exe" => Glyph.Bars,
        "ms-settings:" => Glyph.Gear,
        "control.exe" => Glyph.Windows,
        "ms-screenclip:" => Glyph.Pointer,
        _ => Glyph.Bolt,
    };

    void QuickLaunchTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAction action }) LaunchAction(action);
    }

    void ColorPicker_Click(object sender, RoutedEventArgs e)
    {
        CollapseNow();
        StartColorPicker();
    }

    void ScreenOcr_Click(object sender, RoutedEventArgs e)
    {
        CollapseNow();
        StartScreenOcr();
    }

    void QuickLaunchAdd_Click(object sender, RoutedEventArgs e)
    {
        _collapseTimeout.Cancel();
        _quickLaunchDialog = true;
        EnterTextEntry();
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "Добавить программу",
                Filter = "Программы (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog(this) == true) AddQuickAction(dialog.FileName);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowQuickLaunchNotice("Не удалось открыть выбор файла");
        }
        finally
        {
            _quickLaunchDialog = false;
            LeaveTextEntry();
            ShowPanel(Panel.QuickLaunch);
            // Диалог уводит курсор с островка: не даём карточке схлопнуться сразу,
            // чтобы добавленная строка осталась на виду.
            _collapseTimeout.Cancel();
            _collapseTimeout.Start(LongCollapseDelay);
        }
    }

    void AddQuickAction(string path)
    {
        if (!QuickActions.IsSupported(path))
        {
            ShowQuickLaunchNotice("Можно добавить .exe, .lnk, .bat или .cmd");
            return;
        }

        QuickAction action = QuickActions.FromFile(path);
        List<QuickAction> own = OwnQuickActions().ToList();
        if (own.Any(item => string.Equals(item.Target, action.Target, StringComparison.OrdinalIgnoreCase)))
        {
            ShowQuickLaunchNotice("Уже в списке");
            return;
        }

        own.Add(action);
        Settings.QuickLaunch = QuickActions.Format(own);
        ClearQuickLaunchNotice();
        RefreshQuickLaunchOwn();
    }

    void RefreshQuickLaunchOwn()
    {
        if (QuickLaunchOwn == null) return;

        IReadOnlyList<QuickAction> own = OwnQuickActions();
        QuickLaunchOwn.Children.Clear();
        QuickLaunchEmpty.Visibility = own.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (QuickAction action in own)
        {
            var name = new TextBlock
            {
                Text = action.DisplayName,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var pathLine = new TextBlock
            {
                Text = action.Target,
                FontSize = 9.5,
                Foreground = _dim,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 30, 0) };
            text.Children.Add(name);
            text.Children.Add(pathLine);

            var remove = new Button
            {
                Style = (Style)FindResource("ChipButton"),
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
                Content = "×",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = action,
                ToolTip = "Убрать из списка",
            };
            remove.Click += QuickLaunchRemove_Click;

            var grid = new Grid();
            grid.Children.Add(text);
            grid.Children.Add(remove);

            var row = new Button
            {
                Style = (Style)FindResource("RowButton"),
                Height = 44,
                Content = grid,
                Tag = action,
                ToolTip = action.Target,
            };
            row.Click += QuickLaunchOwn_Click;
            QuickLaunchOwn.Children.Add(row);
        }
    }

    void QuickLaunchOwn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: QuickAction action }) LaunchAction(action);
    }

    void QuickLaunchRemove_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: QuickAction action }) return;

        List<QuickAction> own = OwnQuickActions()
            .Where(item => !string.Equals(item.Target, action.Target, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Settings.QuickLaunch = QuickActions.Format(own);
        RefreshQuickLaunchOwn();
    }

    IReadOnlyList<QuickAction> OwnQuickActions() => QuickActions.Parse(Settings.QuickLaunch);

    async void LaunchAction(QuickAction action)
    {
        // Проверка существования файла и старт процесса не должны блокировать UI
        // (например, если путь указывает на отключённую сетевую папку).
        if (!await Task.Run(() => QuickActions.CanStart(action)))
        {
            ShowQuickLaunchNotice("Файл не найден");
            return;
        }

        try
        {
            await Task.Run(() => Process.Start(QuickActions.StartInfo(action))?.Dispose());
            App.LogDebug($"QuickLaunch: started {action.Target}.");
            CollapseNow();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowQuickLaunchNotice("Не удалось запустить");
        }
    }

    void ShowQuickLaunchNotice(string text)
    {
        QuickLaunchNotice.Text = text;
        (_quickLaunchNotice ??= new DelayedAction(ClearQuickLaunchNotice)).Start(TimeSpan.FromSeconds(4));
    }

    void ClearQuickLaunchNotice() => QuickLaunchNotice.Text = "";
}
