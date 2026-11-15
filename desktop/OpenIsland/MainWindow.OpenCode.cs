using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

public partial class MainWindow
{
    const string PromptPlaceholder = "Дать задачу агенту...";

    static readonly Brush PersonaSelectedBrush = Frozen(Color.FromArgb(0x66, 0x3B, 0x82, 0xF6));
    static readonly Brush PersonaIdleBrush = Frozen(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));

    readonly OpenCodeService _openCode = new();
    string _selectedPersona = "";
    string _openCodeUiSignature = "";
    bool _openCodeReady;

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

        OpenCodeCompactText.Text = _openCode.StatusText;

        string title = _openCode.SessionTitle;
        OpenCodeTaskTitle.Text = string.IsNullOrWhiteSpace(title)
            ? (_openCode.IsConnected ? "Нет активной сессии" : "OpenCode офлайн")
            : title;

        string subtitle = string.IsNullOrWhiteSpace(_openCode.LastError)
            ? _openCode.StatusText
            : _openCode.LastError;
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

        OpenCodeStatusDot.Fill = _openCode.IsConnected
            ? (_openCode.IsBusy ? _orange : _green)
            : _dim;

        if (MenuOpenCodeStatus != null)
        {
            MenuOpenCodeStatus.Text = _openCode.IsConnected
                ? (_openCode.IsBusy ? "В работе" : "В сети")
                : "Офлайн";
            MenuOpenCodeStatus.Foreground = _openCode.IsConnected
                ? (_openCode.IsBusy ? _orange : _green)
                : _dim;
        }

        ApplyPersonaSelection();

        string signature = string.Join('|',
            _openCode.SelectedSessionId,
            _openCode.IsBusy,
            permission?.Id,
            _openCode.Sessions.Count,
            _openCode.IsConnected);

        if (signature == _openCodeUiSignature) return;
        _openCodeUiSignature = signature;
        UpdateTargets();
        UpdateView();
    }

    void ApplyPersonaSelection()
    {
        Button[] buttons = [OpenCodePersonaCode, OpenCodePersonaUi, OpenCodePersonaReview, OpenCodePersonaFix];
        foreach (Button button in buttons)
        {
            bool selected = button.Tag is string tag && tag == _selectedPersona;
            button.Background = selected ? PersonaSelectedBrush : PersonaIdleBrush;
        }
    }

    static string Shorten(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..Math.Max(0, maxLength - 1)] + "…";

    static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";

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

    void OpenCodePersona_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string persona) return;

        _selectedPersona = _selectedPersona == persona ? "" : persona;
        ApplyPersonaSelection();
        OpenCodePromptBox.Focus();
    }

    async void OpenCodeSend_Click(object sender, RoutedEventArgs e)
    {
        if (OpenCodePromptBox == null) return;

        string text = OpenCodePromptBox.Text.Trim();
        if (string.IsNullOrEmpty(text) || text == PromptPlaceholder) return;

        string persona = _selectedPersona;
        OpenCodePromptBox.IsEnabled = false;
        OpenCodePromptBox.Text = "Отправка...";

        try
        {
            bool sent = await _openCode.SendPromptAsync(text, persona);
            OpenCodePromptBox.Text = sent ? "" : text;
            if (sent)
            {
                _selectedPersona = "";
                ApplyPersonaSelection();
            }
        }
        finally
        {
            OpenCodePromptBox.IsEnabled = true;
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
