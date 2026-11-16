using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenIsland;

public partial class MainWindow
{
    const string PromptPlaceholder = "Дать задачу агенту...";

    /// <summary>Задачи короче этого порога уведомлением не сопровождаем — только звуком.</summary>
    static readonly TimeSpan TaskToastAfter = TimeSpan.FromSeconds(15);

    static readonly Brush ChipBackground = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    static readonly Brush AccentBackground = Frozen(Color.FromArgb(0xFF, 0x7D, 0x7A, 0xFF));

    readonly OpenCodeService _openCode = new();
    readonly Chime _chime = new();
    string _openCodeUiSignature = "";
    bool _openCodeReady;
    bool _openCodeBusy;
    bool _openCodeSending;
    DateTime _openCodeBusySinceUtc;

    static Brush Frozen(Color color)
    {
        SolidColorBrush brush = new(color);
        brush.Freeze();
        return brush;
    }

    void InitOpenCode()
    {
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
        if (busy && !_openCodeBusy) _openCodeBusySinceUtc = DateTime.UtcNow;
        if (_openCodeBusy && !busy && online)
        {
            _chime.Play();
            NotifyTaskDone(DateTime.UtcNow - _openCodeBusySinceUtc);
        }
        _openCodeBusy = busy;

        OpenCodeCompactText.Text = _openCode.StatusText;

        string title = _openCode.SessionTitle;
        OpenCodeTaskTitle.Text = string.IsNullOrWhiteSpace(title)
            ? (online ? "Нет активной сессии" : "OpenCode не запущен")
            : title;

        string subtitle = !online && string.IsNullOrWhiteSpace(_openCode.LastError)
            ? "Нажмите «Запустить OpenCode»"
            : (string.IsNullOrWhiteSpace(_openCode.LastError) ? _openCode.StatusText : _openCode.LastError);
        OpenCodeSubText.Text = subtitle;
        OpenCodeSubText.Foreground = string.IsNullOrWhiteSpace(_openCode.LastError)
            ? _dim
            : _orange;

        string modelName = _openCode.ModelName;
        OpenCodeModelBadge.Text = modelName;
        OpenCodeModelName.Text = modelName;

        int sessionIndex = _openCode.SelectedSessionIndex;
        OpenCodeSessionCounter.Text = _openCode.Sessions.Count == 0
            ? "0 / 0"
            : $"{sessionIndex + 1} / {_openCode.Sessions.Count}";

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

    /// <summary>Офлайн-состояние: недоступные действия отключаем, а кнопку запуска переименовываем.</summary>
    void SetOpenCodeAvailable(bool online)
    {
        bool editable = online && !_openCodeSending;

        OpenCodePrevSession.IsEnabled = online;
        OpenCodeNextSession.IsEnabled = online;
        OpenCodeModelPrev.IsEnabled = online;
        OpenCodeModelNext.IsEnabled = online;
        OpenCodeNewSession.IsEnabled = online;
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

    void OpenCodeBack_Click(object sender, RoutedEventArgs e)
    {
        ShowPanel(Panel.Menu);
    }

    void OpenCodePrevSession_Click(object sender, RoutedEventArgs e)
    {
        _openCode.SelectSession(-1);
        SyncOpenCodeUI();
    }

    void OpenCodeNextSession_Click(object sender, RoutedEventArgs e)
    {
        _openCode.SelectSession(1);
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
        if (string.IsNullOrEmpty(text) || text == PromptPlaceholder) return;

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
            OpenCodePromptBox_LostFocus(OpenCodePromptBox, new RoutedEventArgs());
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
        if (_panel != Panel.OpenCode) return;
        if (OpenCodePromptBox.Text == PromptPlaceholder) return;
        _collapseTimeout.Cancel();
        _collapseTimeout.Start(LongCollapseDelay);
    }


    void OpenCodePromptBox_GotFocus(object sender, RoutedEventArgs e)
    {
        EnterTextEntry();

        if (OpenCodePromptBox.Text == PromptPlaceholder)
        {
            OpenCodePromptBox.Text = "";
            OpenCodePromptBox.Foreground = Brushes.White;
        }
    }

    void OpenCodePromptBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(OpenCodePromptBox.Text))
        {
            OpenCodePromptBox.Text = PromptPlaceholder;
            OpenCodePromptBox.Foreground = _dim;
        }
    }
}
