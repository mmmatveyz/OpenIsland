using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenIsland;

/// <summary>
/// Карточка «Процессы и порты»: dev-процессы и владельцы слушающих портов.
/// Опрос идёт только пока карточка открыта (<see cref="DevProcessService"/>).
/// Завершение — в два шага: клик по строке, затем явная кнопка «Завершить».
/// </summary>
public partial class MainWindow
{
    sealed class ProcessRow
    {
        public required Button Button { get; init; }
        public required TextBlock Name { get; init; }
        public required TextBlock Meta { get; init; }
        public required TextBlock Ports { get; init; }
    }

    readonly DevProcessService _devProcesses;
    readonly Dictionary<int, ProcessRow> _processRows = [];
    int _killPid;
    string _killName = "";
    double _confirmHeight;
    bool _showAllProcesses;

    void InitProcesses()
    {
        _devProcesses.Changed += OnProcessesChanged;
        SyncProcessesUI();
    }

    void OnProcessesChanged()
    {
        UpdateProcessesMenu();
        if (_view == View.Processes) SyncProcessesUI();
    }

    void ProcessesRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Processes);

    void ProcessesBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    void ProcessesDev_Click(object sender, RoutedEventArgs e) => SetProcessFilter(false);

    void ProcessesAll_Click(object sender, RoutedEventArgs e) => SetProcessFilter(true);

    void SetProcessFilter(bool all)
    {
        if (_showAllProcesses == all) return;
        _showAllProcesses = all;
        ClearKill();
        SyncProcessesUI();
    }

    /// <summary>Режим «Dev» — только инструменты разработки, «Все» — плюс любой процесс с портом.</summary>
    IReadOnlyList<DevProcessInfo> VisibleProcesses() =>
        _showAllProcesses
            ? _devProcesses.Items
            : _devProcesses.Items.Where(item => DevProcesses.IsKnown(item.Name)).ToList();

    void UpdateProcessesMenu()
    {
        if (MenuProcesses == null) return;
        int busy = _devProcesses.Items.Count(item => item.Ports.Count > 0 && DevProcesses.IsKnown(item.Name));
        MenuProcesses.Text = _devProcesses.HasSample ? DevProcesses.PortsCount(busy) : "";
    }

    void SyncProcessesUI()
    {
        IReadOnlyList<DevProcessInfo> items = VisibleProcesses();
        bool sampled = _devProcesses.HasSample;

        UpdateProcessesMenu();
        SyncProcessChips();
        ProcessesEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ProcessesEmpty.Text = sampled
            ? (_showAllProcesses ? "Нет занятых локальных портов" : "Нет запущенных dev-инструментов")
            : "Сканирую процессы и порты…";

        // Процесс мог завершиться сам, пока открыта карточка.
        if (_killPid != 0 && !items.Any(item => item.Pid == _killPid)) ClearKill();

        foreach (int pid in _processRows.Keys.Where(pid => !items.Any(item => item.Pid == pid)).ToList())
        {
            ProcessesList.Children.Remove(_processRows[pid].Button);
            _processRows.Remove(pid);
        }

        for (int i = 0; i < items.Count; i++)
        {
            DevProcessInfo info = items[i];
            if (!_processRows.TryGetValue(info.Pid, out ProcessRow? row))
            {
                row = CreateProcessRow(info);
                _processRows[info.Pid] = row;
            }

            UpdateProcessRow(row, info);

            int at = ProcessesList.Children.IndexOf(row.Button);
            if (at == i) continue;
            if (at >= 0) ProcessesList.Children.RemoveAt(at);
            ProcessesList.Children.Insert(Math.Min(i, ProcessesList.Children.Count), row.Button);
        }

        FitProcessesCard();
        UpdateTargets();
    }

    /// <summary>Подсвечивает выбранный режим: индиго — активный, прозрачный — второй.</summary>
    void SyncProcessChips()
    {
        if (ProcessesDevChip == null) return;
        SetProcessChip(ProcessesDevChip, !_showAllProcesses);
        SetProcessChip(ProcessesAllChip, _showAllProcesses);
    }

    void SetProcessChip(Button chip, bool on)
    {
        if (on)
        {
            chip.Background = _indigo;
            chip.Foreground = Brushes.White;
        }
        else
        {
            chip.ClearValue(Control.BackgroundProperty);
            chip.Foreground = _dim;
        }
    }

    /// <summary>Высота карточки по числу строк (максимум 8 видны без прокрутки) и панели подтверждения.</summary>
    void FitProcessesCard()
    {
        int rows = Math.Clamp(VisibleProcesses().Count, 0, 8);
        double body = Math.Max(rows * 46 + 2, 92);
        double confirm = ProcessesConfirm.Visibility == Visibility.Visible ? 54 : 0;
        // Минимум 250: при открытии из меню курсор остаётся в пределах карточки,
        // иначе она схлопывается, пока первый замер ещё идёт.
        double height = Math.Clamp(24 + 28 + 8 + body + confirm, 250, 470);

        // Пока открыто подтверждение, высота заморожена: иначе исчезнувший процесс
        // утащит кнопку «Завершить» из-под курсора.
        if (_confirmHeight > 0) height = _confirmHeight;
        ProcessesView.Height = height;
    }

    ProcessRow CreateProcessRow(DevProcessInfo info)
    {
        var name = new TextBlock
        {
            Text = info.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var meta = new TextBlock
        {
            FontSize = 10.5,
            Foreground = _dim,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var ports = new TextBlock
        {
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            TextAlignment = TextAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 0, 0),
            MaxWidth = 118,
        };
        var kill = new Icon
        {
            Kind = Glyph.Cross,
            Width = 9,
            Height = 9,
            Fill = _dim,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
        };

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        text.Children.Add(meta);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 0);
        Grid.SetColumn(ports, 1);
        Grid.SetColumn(kill, 2);
        grid.Children.Add(text);
        grid.Children.Add(ports);
        grid.Children.Add(kill);

        var button = new Button
        {
            Style = (Style)FindResource("RowButton"),
            Height = 46,
            Content = grid,
            Tag = info.Pid,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        button.Click += ProcessesKillRow_Click;

        var row = new ProcessRow { Button = button, Name = name, Meta = meta, Ports = ports };
        UpdateProcessRow(row, info);
        return row;
    }

    void UpdateProcessRow(ProcessRow row, DevProcessInfo info)
    {
        row.Name.Text = info.Name;
        row.Meta.Text = $"PID {info.Pid} · {DevProcesses.FormatMemory(info.WorkingSetBytes)}";
        row.Ports.Text = DevProcesses.FormatPorts(info.Ports);
        row.Ports.Foreground = info.Ports.Count > 0 ? _green : _dim;
        row.Ports.ToolTip = DevProcesses.PortsTooltip(info.Ports);
        row.Button.ToolTip = $"Завершить {info.Name} (PID {info.Pid})";
    }

    void ProcessesKillRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int pid }) return;
        DevProcessInfo? info = _devProcesses.Items.FirstOrDefault(item => item.Pid == pid);
        if (info == null) return;

        _killPid = pid;
        _killName = info.Name;
        ProcessesConfirmText.Foreground = _dim;
        ProcessesConfirmText.Text = $"{info.Name} · PID {pid}";
        ProcessesConfirm.Visibility = Visibility.Visible;
        FitProcessesCard();
        _confirmHeight = ProcessesView.Height;
        UpdateTargets();
    }

    void ProcessesKill_Click(object sender, RoutedEventArgs e)
    {
        if (_killPid == 0) return;

        (bool ok, string error) = _devProcesses.Kill(_killPid);
        if (!ok)
        {
            ProcessesConfirmText.Foreground = _red;
            ProcessesConfirmText.Text = error;
            return;
        }

        App.LogDebug($"Processes: termination requested for {_killName} (PID {_killPid}).");
        ClearKill();
        _devProcesses.Refresh();
    }

    void ProcessesCancel_Click(object sender, RoutedEventArgs e) => ClearKill();

    void ClearKill()
    {
        _killPid = 0;
        _killName = "";
        _confirmHeight = 0;
        if (ProcessesConfirm == null) return;
        ProcessesConfirm.Visibility = Visibility.Collapsed;
        ProcessesConfirmText.Foreground = _dim;
        ProcessesConfirmText.Text = "";
        FitProcessesCard();
        UpdateTargets();
    }
}
