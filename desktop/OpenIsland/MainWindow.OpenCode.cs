using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace OpenIsland;

public partial class MainWindow
{
    /// <summary>Задачи короче этого порога уведомлением не сопровождаем — только звуком.</summary>
    static readonly TimeSpan TaskToastAfter = TimeSpan.FromSeconds(15);

    static readonly Brush ChipBackground = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    static readonly Brush AccentBackground = Frozen(Color.FromArgb(0xFF, 0x7D, 0x7A, 0xFF));
    static readonly Brush DangerBackground = Frozen(Color.FromArgb(0xFF, 0xFF, 0x45, 0x3A));

    readonly OpenCodeService _openCode = new();
    readonly Chime _chime = new();
    string _openCodeUiSignature = "";
    bool _openCodeReady;
    bool _openCodeBusy;
    bool _openCodeSending;
    bool _openCodeDeleteArmed;
    bool _openCodeSessionsOpen;
    bool _openCodeSessionsBuilding;
    string _openCodeSessionsSignature = "";
    string _openCodeNotice = "";
    DispatcherTimer? _openCodeNoticeTimeout;
    DispatcherTimer? _openCodeDeleteTimeout;
    DateTime _openCodeBusySinceUtc;
    DateTime _openCodeDoneAtUtc;
    AgentPillState _agentPill = AgentPillState.Hidden;
    Glyph _compactGlyph = Glyph.Sparkle;
    Brush? _compactTint;

    static Brush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    void InitOpenCode()
    {
        _openCodeDeleteTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _openCodeDeleteTimeout.Tick += (_, _) => DisarmDeleteSession();
        _openCode.StateChanged += OnOpenCodeStateChanged;
        _openCodeReady = true;
        SyncOpenCodeUI();
    }

    void OnOpenCodeStateChanged()
    {
        if (!_openCodeReady) return;
        try
        {
            Dispatcher.BeginInvoke(SyncOpenCodeUI);
        }
        catch
        {
            // Окно уже закрывается.
        }
    }

    void SyncOpenCodeUI()
    {
        if (!_openCodeReady) return;

        bool online = _openCode.IsConnected;
        bool busy = _openCode.IsBusy;
        if (busy && !_openCodeBusy)
        {
            _openCodeBusySinceUtc = DateTime.UtcNow;
            _openCodeDoneAtUtc = default;
        }
        if (_openCodeBusy && !busy && online)
        {
            if (Settings.TaskChime) _chime.Play();
            NotifyTaskDone(DateTime.UtcNow - _openCodeBusySinceUtc);
            _openCodeDoneAtUtc = DateTime.UtcNow;
        }
        _openCodeBusy = busy;

        OpenCodeCompactText.Text = _openCode.StatusText;

        string title = _openCode.SessionTitle;
        OpenCodeTaskTitle.Text = string.IsNullOrWhiteSpace(title)
            ? (online ? "Нет активной сессии" : "OpenCode не запущен")
            : title;

        bool error = !string.IsNullOrWhiteSpace(_openCode.LastError);
        string subtitle = _openCodeNotice.Length > 0
            ? _openCodeNotice
            : !online && !error
                ? "Нажмите «Запустить OpenCode»"
                : error ? _openCode.LastError : _openCode.StatusText;
        OpenCodeSubText.Text = subtitle;
        OpenCodeSubText.Foreground = error && _openCodeNotice.Length == 0 ? _orange : _dim;

        string modelName = _openCode.ModelName;
        OpenCodeModelBadge.Text = modelName;
        OpenCodeModelName.Text = modelName;

        int sessionCount = _openCode.Sessions.Count;
        OpenCodeSessionCounter.Text = sessionCount > 1 ? sessionCount.ToString() : "";

        string answer = _openCode.LastAssistantText.Trim();
        OpenCodeAnswerBox.Visibility = answer.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (answer.Length > 12000) answer = answer[..12000];
        OpenCodeAnswerText.Text = answer;
        if (answer.Length > 0 && _openCode.IsBusy) Dispatcher.BeginInvoke(() => OpenCodeAnswerScroll.ScrollToEnd(), DispatcherPriority.Render);

        UpdateOpenCodeTimer();

        OpenCodePermissionInfo? permission = _openCode.PendingPermission;
        OpenCodePermissionPanel.Visibility = permission != null ? Visibility.Visible : Visibility.Collapsed;
        if (permission != null)
        {
            string resources = string.Join(", ", permission.Resources.Take(3));
            string message = string.IsNullOrWhiteSpace(permission.Message) ? resources : permission.Message!;
            OpenCodePermissionText.Text = $"{permission.Action}: {Shorten(message, 90)}";
        }

        OpenCodeStatusDot.Fill = online
            ? (busy ? _orange : _green)
            : _dim;

        // В компактной капсуле иконка сразу говорит, чего ждёт агент:
        // искра — работа, колокольчик — разрешение.
        Glyph compactGlyph = permission != null ? Glyph.Bell : Glyph.Sparkle;
        Brush compactTint = permission != null ? _orange : _indigo;
        if (compactGlyph != _compactGlyph)
        {
            _compactGlyph = compactGlyph;
            OpenCodeCompactIcon.Kind = compactGlyph;
        }
        if (!ReferenceEquals(compactTint, _compactTint))
        {
            _compactTint = compactTint;
            OpenCodeCompactIcon.Fill = compactTint;
        }

        SyncAgentPill();

        if (MenuOpenCodeStatus != null)
        {
            MenuOpenCodeStatus.Text = online
                ? (busy ? "В работе" : "В сети")
                : "Офлайн";
            MenuOpenCodeStatus.Foreground = online
                ? (busy ? _orange : _green)
                : _dim;
        }

        SetOpenCodeAvailable(online);
        SyncOpenCodeSessions(online);

        string signature = string.Join('|',
            _openCode.SelectedSessionId,
            _openCode.IsBusy,
            permission?.Id,
            _openCode.Sessions.Count,
            online);

        if (signature == _openCodeUiSignature) return;
        _openCodeUiSignature = signature;
        UpdateTargets();
        UpdateView();
    }

    /// <summary>
    /// Свёрнутая пилюля должна говорить о состоянии агента, даже когда остров
    /// не раскрыт: идёт работа, нужно разрешение или задача только что завершилась.
    /// Состояние меняется редко, поэтому UI трогаем только при реальном переходе.
    /// </summary>
    void SyncAgentPill()
    {
        bool asks = _openCode.PendingPermission != null;
        TimeSpan sinceDone = _openCodeDoneAtUtc == default
            ? TimeSpan.MaxValue
            : DateTime.UtcNow - _openCodeDoneAtUtc;
        AgentPillState state = AgentPill.StateOf(_openCode.IsConnected, _openCode.IsBusy, asks, sinceDone);
        if (state == _agentPill) return;
        _agentPill = state;

        (Glyph glyph, Brush brush, string text) = state switch
        {
            AgentPillState.Asks => (Glyph.Bell, (Brush)_orange, "Требуется разрешение"),
            AgentPillState.Busy => (Glyph.Sparkle, _indigo, "Агент работает"),
            AgentPillState.Done => (Glyph.Check, (Brush)_green, "Задача выполнена"),
            _ => (Glyph.Sparkle, (Brush)_dim, ""),
        };
        Visibility shown = state == AgentPillState.Hidden ? Visibility.Collapsed : Visibility.Visible;

        if (IdleAgentIcon.Kind != glyph) IdleAgentIcon.Kind = glyph;
        IdleAgentIcon.Fill = brush;
        IdleAgentIcon.Visibility = shown;

        if (BigAgentIcon.Kind != glyph) BigAgentIcon.Kind = glyph;
        BigAgentIcon.Fill = brush;
        BigAgentText.Text = text;
        BigAgentText.Foreground = brush;
        BigAgentRow.Visibility = shown;
    }

    /// <summary>Офлайн-состояние: недоступные действия отключаем, а кнопку запуска переименовываем.</summary>
    void SetOpenCodeAvailable(bool online)
    {
        bool editable = online && !_openCodeSending;

        OpenCodeSessionHeader.IsEnabled = online;
        OpenCodeModelPrev.IsEnabled = online;
        OpenCodeModelNext.IsEnabled = online;
        OpenCodeNewSession.IsEnabled = online;
        OpenCodeDeleteSession.IsEnabled = online && !_openCode.IsBusy && !string.IsNullOrWhiteSpace(_openCode.SelectedSessionId);
        if (!OpenCodeDeleteSession.IsEnabled) DisarmDeleteSession();
        OpenCodeStopButton.IsEnabled = online;
        OpenCodeSendIcon.IsEnabled = editable;
        OpenCodeSendButton.IsEnabled = editable;
        OpenCodePromptBox.IsEnabled = editable;

        OpenCodeAppButton.Content = online ? "OpenCode Desktop" : "Запустить OpenCode";
        OpenCodeAppButton.Background = online ? ChipBackground : AccentBackground;
    }

    static string Shorten(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

    /// <summary>Системное уведомление о завершении длинной задачи агента (если включено в настройках).</summary>
    void NotifyTaskDone(TimeSpan duration)
    {
        if (!Settings.TaskToast || duration < TaskToastAfter || !_trayAdded) return;

        string title = string.IsNullOrWhiteSpace(_openCode.SessionTitle) ? "OpenCode" : _openCode.SessionTitle;
        string text = $"Задача заняла {FormatElapsed(duration)}";
        Native.ShowBalloon(_hwnd, TrayId, title, text);
        App.LogDebug($"Toast: task finished in {FormatElapsed(duration)}.");
    }

    void UpdateOpenCodeTimer()
    {
        if (_openCode.IsBusy && _openCode.TaskStartedAtUtc.HasValue)
        {
            string elapsed = $"\u23F1 {FormatElapsed(DateTime.UtcNow - _openCode.TaskStartedAtUtc.Value)}";
            OpenCodeTaskTimer.Visibility = Visibility.Visible;
            OpenCodeTaskTimer.Text = elapsed;
            OpenCodeCompactTimer.Visibility = Visibility.Visible;
            OpenCodeCompactTimer.Text = elapsed;
        }
        else
        {
            OpenCodeTaskTimer.Visibility = Visibility.Collapsed;
            OpenCodeCompactTimer.Visibility = Visibility.Collapsed;
        }
    }

    void OpenCodeRow_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(Panel.OpenCode);
    }

    void PlayerRow_Click(object sender, RoutedEventArgs e)
    {
        if (_media.HasTrack) ShowPanel(Panel.Player);
    }

    void OpenCodeBack_Click(object sender, RoutedEventArgs e)
    {
        CloseOpenCodeSessions();
        ShowPanel(Panel.Menu);
    }

    void OpenCodeSessionHeader_Click(object sender, RoutedEventArgs e)
    {
        if (!_openCode.IsConnected) return;
        if (_openCodeSessionsOpen) CloseOpenCodeSessions();
        else OpenOpenCodeSessions();
    }

    void OpenCodeSessionClose_Click(object sender, RoutedEventArgs e) => CloseOpenCodeSessions();

    void OpenCodeSessionScrim_MouseDown(object sender, MouseButtonEventArgs e)
    {
        CloseOpenCodeSessions();
        e.Handled = true;
    }

    void OpenCodeSessionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OpenCodeSessionSearchHint != null)
        {
            OpenCodeSessionSearchHint.Visibility = string.IsNullOrEmpty(OpenCodeSessionSearch.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_openCodeSessionsBuilding || !_openCodeSessionsOpen) return;
        RebuildSessionRows();
    }

    void OpenCodeSessionRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        CloseOpenCodeSessions();
        _openCode.SelectSession(id);
        SyncOpenCodeUI();
    }

    void OpenOpenCodeSessions()
    {
        _openCodeSessionsOpen = true;
        _openCodeSessionsSignature = "";
        RebuildSessionRows();
        _openCodeSessionsSignature = SessionSignature();
        OpenCodeSessionOverlay.Visibility = Visibility.Visible;
        OpenCodeSessionChevron.RenderTransform = new RotateTransform(90);
    }

    void CloseOpenCodeSessions()
    {
        _openCodeSessionsOpen = false;
        OpenCodeSessionOverlay.Visibility = Visibility.Collapsed;
        OpenCodeSessionChevron.RenderTransform = new RotateTransform(0);
    }

    /// <summary>Перестраивает список, только пока он открыт и его содержимое изменилось.</summary>
    void SyncOpenCodeSessions(bool online)
    {
        if (!_openCodeSessionsOpen) return;
        if (!online)
        {
            CloseOpenCodeSessions();
            return;
        }

        string signature = SessionSignature();
        if (signature == _openCodeSessionsSignature) return;
        _openCodeSessionsSignature = signature;
        RebuildSessionRows();
    }

    string SessionSignature() => string.Join('|',
        _openCode.Sessions.Select(session => $"{session.Id}:{session.UpdatedAt}:{session.IsRunning}:{session.Title}"));

    void RebuildSessionRows()
    {
        if (OpenCodeSessionList == null) return;

        _openCodeSessionsBuilding = true;
        try
        {
            IReadOnlyList<OpenCodeSessionInfo> all = _openCode.Sessions;
            bool searchable = all.Count >= SessionList.SearchThreshold;
            OpenCodeSessionSearchBox.Visibility = searchable ? Visibility.Visible : Visibility.Collapsed;
            if (!searchable && OpenCodeSessionSearch.Text.Length > 0) OpenCodeSessionSearch.Text = "";

            List<OpenCodeSessionInfo> rows = SessionList.Filter(all, searchable ? OpenCodeSessionSearch.Text : "");
            string? currentId = _openCode.SelectedSessionId;
            DateTime now = DateTime.UtcNow;

            OpenCodeSessionList.Children.Clear();
            foreach (OpenCodeSessionInfo session in rows)
                OpenCodeSessionList.Children.Add(BuildSessionRow(session, currentId, now));

            OpenCodeSessionEmpty.Text = all.Count == 0 ? "Сессий пока нет" : "Ничего не найдено";
            OpenCodeSessionEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _openCodeSessionsBuilding = false;
        }
    }

    Button BuildSessionRow(OpenCodeSessionInfo session, string? currentId, DateTime now)
    {
        bool current = session.Id == currentId;
        string folder = SessionList.FolderName(session.Directory);
        string time = SessionList.RelativeTime(session.UpdatedAt, now);
        string detail = folder.Length == 0 ? time : time.Length == 0 ? folder : $"{folder} · {time}";
        if (session.IsRunning && !current)
            detail = detail.Length == 0 ? "работает" : "работает · " + detail;

        Ellipse dot = new()
        {
            Width = 7,
            Height = 7,
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = current ? _indigo : session.IsRunning ? _orange : Brushes.Transparent,
        };

        StackPanel text = new() { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = session.Title,
            FontSize = 12.5,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        if (detail.Length > 0)
        {
            text.Children.Add(new TextBlock
            {
                Text = detail,
                FontSize = 10.5,
                Foreground = _dim,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(dot, 0);
        Grid.SetColumn(text, 1);
        content.Children.Add(dot);
        content.Children.Add(text);

        Button row = new()
        {
            Style = (Style)FindResource("SessionRowButton"),
            Tag = session.Id,
            Content = content,
            Margin = new Thickness(0, 0, 0, 2),
        };
        row.Click += OpenCodeSessionRow_Click;
        return row;
    }

    void ArmDeleteSession()
    {
        _openCodeDeleteArmed = true;
        OpenCodeDeleteSession.Background = DangerBackground;
        OpenCodeDeleteSession.Foreground = Brushes.White;
        OpenCodeDeleteSession.ToolTip = "Нажмите ещё раз, чтобы удалить сессию";
        _openCodeDeleteTimeout?.Stop();
        _openCodeDeleteTimeout?.Start();
    }

    void DisarmDeleteSession()
    {
        _openCodeDeleteTimeout?.Stop();
        if (!_openCodeDeleteArmed) return;

        _openCodeDeleteArmed = false;
        OpenCodeDeleteSession.ClearValue(Control.BackgroundProperty);
        OpenCodeDeleteSession.ClearValue(Control.ForegroundProperty);
        OpenCodeDeleteSession.ToolTip = "Удалить текущую сессию";
    }

    /// <summary>Короткое сообщение под заголовком сессии, которое гаснет само.</summary>
    void ShowOpenCodeNotice(string text)
    {
        _openCodeNotice = text;

        if (_openCodeNoticeTimeout == null)
        {
            _openCodeNoticeTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _openCodeNoticeTimeout.Tick += (_, _) =>
            {
                _openCodeNoticeTimeout!.Stop();
                _openCodeNotice = "";
                SyncOpenCodeUI();
            };
        }

        _openCodeNoticeTimeout.Stop();
        _openCodeNoticeTimeout.Start();
        SyncOpenCodeUI();
    }

    async void OpenCodeDeleteSession_Click(object sender, RoutedEventArgs e)
    {
        string? sessionId = _openCode.SelectedSessionId;
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        if (!_openCodeDeleteArmed)
        {
            ArmDeleteSession();
            return;
        }

        DisarmDeleteSession();
        OpenCodeSubText.Text = "Удаление сессии...";
        bool deleted = await _openCode.DeleteSessionAsync(sessionId);
        if (!deleted)
        {
            OpenCodeSubText.Text = string.IsNullOrWhiteSpace(_openCode.LastError)
                ? "Не удалось удалить сессию"
                : _openCode.LastError;
            return;
        }

        await _openCode.RefreshAsync();
        ShowOpenCodeNotice(string.IsNullOrWhiteSpace(_openCode.SelectedSessionId)
            ? "Сессия удалена"
            : "Сессия удалена — выбрана другая");
        SyncOpenCodeUI();
    }

    async void OpenCodeNewSession_Click(object sender, RoutedEventArgs e)
    {
        OpenCodeTaskTitle.Text = "Создание сессии...";
        bool created = await _openCode.CreateSessionAsync();
        if (!created)
        {
            OpenCodeSubText.Text = string.IsNullOrWhiteSpace(_openCode.LastError)
                ? "Не удалось создать сессию"
                : _openCode.LastError;
        }
        SyncOpenCodeUI();
    }

    async void OpenCodeModelPrev_Click(object sender, RoutedEventArgs e)
    {
        await _openCode.CycleModelAsync(-1);
        SyncOpenCodeUI();
    }

    async void OpenCodeModelNext_Click(object sender, RoutedEventArgs e)
    {
        await _openCode.CycleModelAsync(1);
        SyncOpenCodeUI();
    }

    async void OpenCodeStop_Click(object sender, RoutedEventArgs e)
    {
        await _openCode.InterruptAsync();
        SyncOpenCodeUI();
    }

    async void OpenCodeAllow_Click(object sender, RoutedEventArgs e)
    {
        await _openCode.ReplyPermissionAsync("once");
        SyncOpenCodeUI();
    }

    async void OpenCodeDeny_Click(object sender, RoutedEventArgs e)
    {
        await _openCode.ReplyPermissionAsync("reject");
        SyncOpenCodeUI();
    }

    void OpenCodeApp_Click(object sender, RoutedEventArgs e)
    {
        _openCode.OpenApp();
    }

    async void OpenCodeSend_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCodePromptBox == null) return;
        if (!_openCode.IsConnected) return;

        string text = OpenCodePromptBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        _openCodeSending = true;
        OpenCodePromptBox.IsEnabled = false;
        OpenCodePromptBox.Text = "Отправка...";

        try
        {
            bool sent = await _openCode.SendPromptAsync(text);
            OpenCodePromptBox.Text = sent ? "" : text;
        }
        finally
        {
            _openCodeSending = false;
            SyncOpenCodeUI();
        }
    }

    void OpenCodePromptBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            OpenCodeSend_Click(sender, e);
        }
    }

    void OpenCodePromptBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        EnterTextEntry();
        OpenCodePromptBox.Focus();
    }

    /// <summary>Пока пользователь набирает задачу, продлеваем таймер сворачивания.</summary>
    void OpenCodePromptBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OpenCodePromptHint == null) return;
        bool empty = string.IsNullOrEmpty(OpenCodePromptBox.Text);
        OpenCodePromptHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty && _panel == Panel.OpenCode)
        {
            _collapseTimeout.Cancel();
            _collapseTimeout.Start(LongCollapseDelay);
        }
    }


    void OpenCodePromptBox_GotFocus(object sender, RoutedEventArgs e)
    {
        EnterTextEntry();
    }
}
