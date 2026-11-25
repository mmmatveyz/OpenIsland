using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace OpenIsland;

public partial class MainWindow
{
    Point _shelfDragStart;
    bool _isShelfDragging;
    bool _shelfDragOut;
    bool _shelfRestoreScroll;

    void InitFileShelf()
    {
        FileShelf.Changed += OnFileShelfChanged;
        FileShelf.AddFiles(Settings.ShelfPaths.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        // Если все сохранённые файлы уже удалены, AddFiles ничего не добавит и не вызовет
        // Changed — чистим реестр от мёртвых путей вручную.
        PersistShelf();
        SyncFileShelfUI();
    }

    void OnFileShelfChanged()
    {
        PersistShelf();
        Dispatcher.Invoke(SyncFileShelfUI);
    }

    void PersistShelf() =>
        Settings.ShelfPaths = string.Join('\n', FileShelf.All.Select(item => item.Path));

    void SyncFileShelfUI()
    {
        IReadOnlyList<FileShelfItem> items = FileShelf.All;
        int count = items.Count;

        double scrollOffset = 0;
        if (_shelfRestoreScroll && ShelfScroll != null)
        {
            scrollOffset = ShelfScroll.VerticalOffset;
        }
        _shelfRestoreScroll = false;

        if (MenuFileShelfCount != null)
        {
            MenuFileShelfCount.Text = count > 0 ? $"{count} шт." : "";
        }

        if (ShelfCountText != null)
        {
            ShelfCountText.Text = count > 0 ? $"{count} из {FileShelf.MaxCapacity}" : "";
        }

        if (ShelfEmpty != null)
        {
            ShelfEmpty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (ShelfList == null) return;
        ShelfList.Children.Clear();

        foreach (FileShelfItem item in items)
        {
            var row = BuildShelfRow(item);
            ShelfList.Children.Add(row);
        }

        if (scrollOffset > 0 && ShelfScroll != null)
        {
            // Даём списку пересчитаться, чтобы восстановить позицию после удаления строки.
            Dispatcher.InvokeAsync(() => ShelfScroll.ScrollToVerticalOffset(scrollOffset), DispatcherPriority.Background);
        }
    }

    Border BuildShelfRow(FileShelfItem item)
    {
        var grid = new Grid { Height = 42 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new Icon
        {
            Kind = item.IsDirectory ? Glyph.Folder : Glyph.Lines,
            Width = 16,
            Height = 16,
            Fill = item.IsDirectory ? _orange : _indigo,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);

        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
        };
        var nameBlock = new TextBlock
        {
            Text = item.Name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = item.Exists ? _textPrimary : _dim,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var sizeBlock = new TextBlock
        {
            Text = item.IsDirectory ? "Папка" : item.Exists ? item.FormattedSize : "Файл удалён или перемещён",
            FontSize = 10,
            Foreground = item.Exists ? _dim : _orange,
        };
        textStack.Children.Add(nameBlock);
        textStack.Children.Add(sizeBlock);
        Grid.SetColumn(textStack, 1);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        var copyBtn = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Content = new Icon { Kind = Glyph.Check, Width = 11, Height = 11, Fill = ThemeService.PrimaryBrush },
            ToolTip = "Скопировать путь к файлу",
            Margin = new Thickness(2, 0, 2, 0),
        };
        copyBtn.Click += (_, e) =>
        {
            e.Handled = true;
            try
            {
                Clipboard.SetText(item.Path);
                Notify(Glyph.Check, _green, "Путь скопирован", item.Name, seconds: 2);
            }
            catch (Exception ex)
            {
                App.LogDebug($"FileShelf: clipboard copy error: {ex.Message}");
            }
        };

        var openBtn = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Content = new Icon { Kind = Glyph.Pointer, Width = 11, Height = 11, Fill = ThemeService.PrimaryBrush },
            ToolTip = "Показать в Проводнике",
            Margin = new Thickness(2, 0, 2, 0),
        };
        openBtn.Click += (_, e) =>
        {
            e.Handled = true;
            try
            {
                if (File.Exists(item.Path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true });
                }
                else if (Directory.Exists(item.Path))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{item.Path}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                App.LogDebug($"FileShelf: open in explorer error: {ex.Message}");
            }
        };

        var removeBtn = new Button
        {
            Style = (Style)FindResource("ChipButton"),
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Content = new Icon { Kind = Glyph.Cross, Width = 11, Height = 11, Fill = _dim },
            ToolTip = "Убрать с полки",
            Margin = new Thickness(2, 0, 2, 0),
        };
        removeBtn.Click += (_, e) =>
        {
            e.Handled = true;
            _shelfRestoreScroll = FileShelf.Remove(item.Path);
            SoundFx.Play(SoundCue.Tuck);
        };

        actions.Children.Add(copyBtn);
        actions.Children.Add(openBtn);
        actions.Children.Add(removeBtn);
        Grid.SetColumn(actions, 2);

        grid.Children.Add(icon);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);

        var row = new Border
        {
            Background = _cardSectionBackground,
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 2, 0, 2),
            Padding = new Thickness(4, 2, 4, 2),
            Child = grid,
            Cursor = Cursors.Hand,
            ToolTip = "Потяните мышью, чтобы перетащить файл в Discord, Telegram или папку",
        };

        // Поддержка перетаскивания файла с полки наружу (DoDragDrop)
        row.PreviewMouseLeftButtonDown += (s, e) =>
        {
            _shelfDragStart = e.GetPosition(row);
            _isShelfDragging = true;
        };

        row.PreviewMouseMove += (s, e) =>
        {
            if (!_isShelfDragging || e.LeftButton != MouseButtonState.Pressed) return;
            Point current = e.GetPosition(row);
            Vector diff = _shelfDragStart - current;
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                _isShelfDragging = false;
                if (item.Exists)
                {
                    var data = new DataObject(DataFormats.FileDrop, new string[] { item.Path });
                    try
                    {
                        // Пока файл тащат в другую программу, курсор вне островка: не даём
                        // таймеру свернуть карточку посреди drag-out.
                        _shelfDragOut = true;
                        _collapseTimeout.Cancel();
                        DragDrop.DoDragDrop(row, data, DragDropEffects.Copy | DragDropEffects.Move);
                    }
                    catch (Exception ex)
                    {
                        App.LogDebug($"FileShelf: DoDragDrop error: {ex.Message}");
                    }
                    finally
                    {
                        _shelfDragOut = false;
                        _collapseTimeout.Cancel();
                        if (!_hovered && _panel == Panel.Shelf) _collapseTimeout.Start(CollapseDelay);
                    }
                }
            }
        };

        row.PreviewMouseLeftButtonUp += (_, _) => _isShelfDragging = false;

        return row;
    }

    void ShelfRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Shelf);

    void ShelfBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    void ShelfClear_Click(object sender, RoutedEventArgs e)
    {
        FileShelf.Clear();
        SoundFx.Play(SoundCue.Tuck);
    }

    void ShelfAdd_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Добавить файлы на полку",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
        {
            int added = FileShelf.AddFiles(dialog.FileNames);
            if (added > 0)
            {
                SoundFx.Play(SoundCue.Pop);
                Notify(Glyph.Folder, _indigo, "Файлы на полке", $"{added} шт. добавлено", seconds: 2);
            }
        }
    }
}
