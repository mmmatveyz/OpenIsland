using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using static OpenIsland.Motion;

namespace OpenIsland;

public partial class MainWindow
{
    const double UpdatePagePadding = 14, UpdateNotesWheelStep = 54;
    const double EdgeFadeLength = 16;
    const int MegabyteShift = 20;
    const double LookHeight = 430, LookTilesHeight = 72, LookWheelStep = 80;
    const double LookEdgeZone = 70, LookEdgeScale = 0.8;
    const double SettingsHeight = 496, SettingsWheelStep = 80;
    const double SettingsEdgeZone = 70, SettingsEdgeScale = 0.8;
    const int LargestScale = 200, ScaleStep = 5;
    const int LargestGap = 24, GapStep = 2;

    Border? _openLookTiles;
    readonly Spring _lookScroll = new(0, 260, 30);
    readonly Spring _settingsScroll = new(0, 260, 30);
    readonly Spring _updateNotesScroll = new(0, 260, 30);

    void SettingsRow_Click(object sender, RoutedEventArgs e)
    {
        _settingsScroll.Snap(0);
        ShowPanelAndCheckUpdate(Panel.Settings);
    }

    void UpdateRow_Click(object sender, RoutedEventArgs e) => ShowPanelAndCheckUpdate(Panel.Update);

    void UpdateBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Settings);

    void LookRow_Click(object sender, RoutedEventArgs e)
    {
        _lookScroll.Snap(0);
        ShowPanel(Panel.Look);
    }

    void SettingsBack_Click(object sender, RoutedEventArgs e)
    {
        _settingsScroll.Snap(0);
        ShowPanel(Panel.Menu);
    }

    void Exit_Click(object sender, RoutedEventArgs e) => Exit();

    void ShowPanelAndCheckUpdate(Panel panel)
    {
        ShowPanel(panel);
        _ = _updater.CheckAsync();
    }

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try { Autostart.Set(!Autostart.Enabled); }
        catch (Exception ex) { App.Log(ex); }
        UpdateSwitches(true);
        SoundFx.Play(SoundCue.Click);
    }

    void Rim_Click(object sender, RoutedEventArgs e)
    {
        Settings.Rim = !Settings.Rim;
        UpdateSwitches(true);
        SyncRim();
        SoundFx.Play(SoundCue.Click);
    }

    void AppVolume_Click(object sender, RoutedEventArgs e)
    {
        Settings.AppVolume = !Settings.AppVolume;
        UpdateSwitches(true);
        SoundFx.Play(SoundCue.Click);
    }

    void Network_Click(object sender, RoutedEventArgs e)
    {
        Settings.Network = !Settings.Network;
        UpdateSwitches(true);
        SoundFx.Play(SoundCue.Click);
    }

    void TaskToast_Click(object sender, RoutedEventArgs e)
    {
        Settings.TaskToast = !Settings.TaskToast;
        UpdateSwitches(true);
        SoundFx.Play(SoundCue.Click);
    }

    void TaskChime_Click(object sender, RoutedEventArgs e)
    {
        Settings.TaskChime = !Settings.TaskChime;
        UpdateSwitches(true);
        SoundFx.Play(SoundCue.Click);
    }

    /// <summary>Клик по строке переключает время жизни карточки по кругу пресетов.</summary>
    void NoticeTime_Click(object sender, RoutedEventArgs e)
    {
        Settings.NoticeSeconds = Settings.NextNoticeSeconds(Settings.NoticeSeconds);
        UpdateSwitches(true);
        SoundFx.Play(SoundCue.Click);
    }

    void Clipboard_Click(object sender, RoutedEventArgs e)
    {
        Settings.Clipboard = !Settings.Clipboard;
        if (!Settings.Clipboard) _clipboard.Clear();
        UpdateSwitches(true);
        RefreshClipboardUI();
        SoundFx.Play(SoundCue.Click);
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        Settings.HideFullscreen = !Settings.HideFullscreen;
        UpdateSwitches(true);
        CheckFullscreen();
        SoundFx.Play(SoundCue.Click);
    }

    void HideFromCapture_Click(object sender, RoutedEventArgs e)
    {
        Settings.HideFromCapture = !Settings.HideFromCapture;
        UpdateSwitches(true);
        ApplyHideFromCapture();
        SoundFx.Play(SoundCue.Click);
    }

    void UiSoundFx_Click(object sender, RoutedEventArgs e)
    {
        Settings.UiSoundFx = !Settings.UiSoundFx;
        UpdateSwitches(true);
        if (Settings.UiSoundFx) SoundFx.Play(SoundCue.Click);
    }

    void UpdateSwitches(bool animate)
    {
        RimSwitch.Set(Settings.Rim, animate);
        AppVolumeSwitch.Set(Settings.AppVolume, animate);
        NetworkSwitch.Set(Settings.Network, animate);
        TaskToastSwitch.Set(Settings.TaskToast, animate);
        TaskChimeSwitch.Set(Settings.TaskChime, animate);
        NoticeTimeValue.Text = $"{Settings.NoticeSeconds} с";
        ClipboardSwitch.Set(Settings.Clipboard, animate);
        FullscreenSwitch.Set(Settings.HideFullscreen, animate);
        HideFromCaptureSwitch.Set(Settings.HideFromCapture, animate);
        UiSoundFxSwitch.Set(Settings.UiSoundFx, animate);
        AutostartSwitch.Set(Autostart.Enabled, animate);
    }

    async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!Updater.IsConfigured) return;
        if (_updater.State != Updater.Stage.Available) await _updater.CheckAsync(true);
        else if (await _updater.InstallAsync()) Exit();
    }

    void RefreshUpdatePage()
    {
        Updater.Stage stage = _updater.State;
        bool configured = stage != Updater.Stage.NotConfigured;
        bool loading = stage == Updater.Stage.Loading, found = loading || stage == Updater.Stage.Available;
        Version version = found ? _updater.LatestVersion! : Updater.CurrentVersion;

        UpdateText.Foreground = found ? _orange : _dim;
        UpdateText.Text = loading ? _updater.Percent + "%" : configured ? "v" + version : "выкл.";

        UpdateVersion.Text = version.ToString();
        UpdateFrom.Text = stage switch
        {
            Updater.Stage.NotConfigured => "обновление не настроено",
            Updater.Stage.Checking => "проверяю…",
            Updater.Stage.Latest => "последняя версия",
            Updater.Stage.Failed => "не получилось",
            _ => found ? "вместо " + Updater.CurrentVersion : "",
        };
        UpdateNotes.SetVisible(found && _updater.Notes.Length > 0);
        string notes = string.Join('\n', _updater.Notes.Select(note => "·  " + note));
        if (UpdateNotesText.Text != notes) _updateNotesScroll.Snap(0);
        UpdateNotesText.Text = notes;

        UpdateButton.SetVisible(!loading);
        UpdateButton.IsEnabled = configured;
        UpdateLoad.SetVisible(loading);
        UpdateButton.Content = stage switch
        {
            Updater.Stage.NotConfigured => "Обновление не настроено",
            Updater.Stage.Available => "Обновить и перезапустить",
            Updater.Stage.Checking => "Проверяю…",
            Updater.Stage.Failed => "Попробовать ещё раз",
            _ => "Проверить ещё раз",
        };
        if (found) UpdateButton.Background = _orange;
        else UpdateButton.ClearValue(BackgroundProperty);
        UpdateButton.Foreground = found ? Brushes.Black : Brushes.White;

        if (loading) ShowUpdateProgress();
        FitUpdatePage();
        UpdateView();
    }

    void ShowUpdateProgress()
    {
        UpdateBytes.Text = $"{_updater.DownloadedBytes >> MegabyteShift} из {_updater.TotalBytes >> MegabyteShift} МБ";
        LoadingText.Text = _updater.Percent + "%";
        var fill = new DoubleAnimation(_updater.Percent / 100.0, Ms(200));
        UpdateRing.BeginAnimation(Ring.ProgressProperty, fill);
        LoadingRing.BeginAnimation(Ring.ProgressProperty, fill);
    }

    void FitUpdatePage()
    {
        UpdateBody.Measure(new Size(UpdatePage.Width, double.PositiveInfinity));
        double height = Math.Ceiling(UpdateBody.DesiredSize.Height) + UpdatePagePadding;
        if (height == UpdatePage.Height) return;

        UpdatePage.Height = height;
        if (_view == View.Update) UpdateTargets();
    }

    double UpdateNotesOverflow => Math.Max(UpdateNotesText.ActualHeight - UpdateNotesBox.ActualHeight, 0);

    void ScrollUpdateNotes(int notches)
    {
        _updateNotesScroll.Target = Math.Clamp(_updateNotesScroll.Target + notches * UpdateNotesWheelStep, 0, UpdateNotesOverflow);
        StartShapeLoop();
    }

    void ApplyUpdateNotesScroll()
    {
        double scrolled = Math.Min(_updateNotesScroll.Value, UpdateNotesOverflow);
        UpdateNotesMove.Y = -scrolled;
        UpdateNotesEdgeTop.Color = EdgeFade(scrolled);
        UpdateNotesEdgeBottom.Color = EdgeFade(UpdateNotesOverflow - scrolled);
    }

    static Color EdgeFade(double past) => Color.FromArgb((byte)Math.Round(255 * (1 - Math.Clamp(past / EdgeFadeLength, 0, 1))), 0, 0, 0);

    void SizeSlider_Changed(object? sender, EventArgs e) => SetScale((int)SizeSlider.Value);

    void GapSlider_Changed(object? sender, EventArgs e) => SetGap((int)GapSlider.Value);

    void PillOpacitySlider_Changed(object? sender, EventArgs e) => SetPillOpacity((int)PillOpacitySlider.Value);

    void OpenOpacitySlider_Changed(object? sender, EventArgs e) => SetOpenOpacity((int)OpenOpacitySlider.Value);

    void SetScale(int percent)
    {
        int was = Settings.Scale;
        Settings.Scale = percent;
        if (Settings.Scale != was) ApplyLook();
    }

    void SetGap(int px)
    {
        int was = Settings.Gap;
        Settings.Gap = px;
        if (Settings.Gap != was) ApplyLook();
    }

    void SetPillOpacity(int percent)
    {
        int was = Settings.PillOpacity;
        Settings.PillOpacity = percent;
        if (Settings.PillOpacity != was) ApplyLook();
    }

    void SetOpenOpacity(int percent)
    {
        int was = Settings.OpenOpacity;
        Settings.OpenOpacity = percent;
        if (Settings.OpenOpacity != was) ApplyLook();
    }

    void Dots_Click(object sender, RoutedEventArgs e)
    {
        Eq.Dots = Settings.Dots = DotsSegments.PickUnderPointer() == 1;
        RefreshLookPage();
    }

    void Glass_Click(object sender, RoutedEventArgs e)
    {
        Settings.Glass = !Settings.Glass;
        RefreshLookPage();
        SyncGlass(true);
    }

    void SeekHover_Click(object sender, RoutedEventArgs e)
    {
        Settings.SeekHover = (SeekHover)SeekHoverSegments.PickUnderPointer();
        RefreshLookPage();
    }

    void BackdropRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(BackdropTiles);

    void HoverRow_Click(object sender, RoutedEventArgs e) => ToggleLookTiles(HoverTiles);

    void BackdropTile_Click(object sender, RoutedEventArgs e)
    {
        Settings.Backdrop = (Backdrop)BackdropStrip.Children.IndexOf((UIElement)sender);
        RefreshLookPage();
        SyncBackdrop();
    }

    void HoverTile_Click(object sender, RoutedEventArgs e)
    {
        Settings.Hover = (Hover)HoverStrip.Children.IndexOf((UIElement)sender);
        RefreshLookPage();
    }

    void ToggleLookTiles(Border tiles)
    {
        _openLookTiles = tiles == _openLookTiles ? null : tiles;
        SlideLookTiles(BackdropTiles, BackdropChevron);
        SlideLookTiles(HoverTiles, HoverChevron);
    }

    void SlideLookTiles(Border tiles, Icon chevron)
    {
        bool open = tiles == _openLookTiles;
        if (!open && tiles.Visibility != Visibility.Visible) return;

        tiles.Visibility = Visibility.Visible;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation(open ? LookTilesHeight : 0, Ms(open ? 320 : 240)) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            if (tiles != _openLookTiles) tiles.Visibility = Visibility.Collapsed;
        };
        tiles.BeginAnimation(HeightProperty, slide);
        tiles.Child.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, Ms(open ? 260 : 160)));
        chevron.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(open ? 90 : 0, Ms(240)) { EasingFunction = ease });
    }

    double LookOverflow => Math.Max(LookRows.ActualHeight + LookRows.Margin.Top + LookRows.Margin.Bottom - LookView.Height, 0);

    void LookRows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double reveal = _openLookTiles == null ? 0
            : _openLookTiles.TranslatePoint(new Point(0, _openLookTiles.ActualHeight), LookRows).Y + LookRows.Margin.Top + LookRows.Margin.Bottom - LookView.Height;
        _lookScroll.Target = Math.Clamp(Math.Max(_lookScroll.Target, reveal), 0, LookOverflow);
        StartShapeLoop();
    }

    void ScrollLook(int notches)
    {
        _lookScroll.Target = Math.Clamp(_lookScroll.Target + notches * LookWheelStep, 0, LookOverflow);
        StartShapeLoop();
    }

    void ShrinkLookEdges(double scrolled)
    {
        double top = Math.Clamp(scrolled / LookEdgeZone, 0, 1), bottom = Math.Clamp((LookOverflow - scrolled) / LookEdgeZone, 0, 1);
        foreach (FrameworkElement row in LookRows.Children)
        {
            double middle = VisualTreeHelper.GetOffset(row).Y + LookRows.Margin.Top + row.ActualHeight / 2 - scrolled;
            double fromTop = Math.Clamp(middle / LookEdgeZone, 0, 1), fromBottom = Math.Clamp((LookView.Height - middle) / LookEdgeZone, 0, 1);
            double shrink = Math.Max(top * Smooth(1 - fromTop), bottom * Smooth(1 - fromBottom));

            if (row.RenderTransform is not ScaleTransform size)
            {
                row.RenderTransform = size = new ScaleTransform();
                row.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            size.ScaleX = size.ScaleY = 1 - (1 - LookEdgeScale) * shrink;
        }
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    void ApplyLookScroll()
    {
        double scrolled = _lookScroll.Value;
        LookEdgeTop.Color = EdgeFade(scrolled);
        LookEdgeBottom.Color = EdgeFade(LookOverflow - scrolled);
        if (LookView.IsVisible) ShrinkLookEdges(scrolled);
        if (LookMove.Y == -scrolled) return;

        LookMove.Y = -scrolled;
        if (_view == View.Look) LookRows.FollowPointer();
    }

    double SettingsOverflow => Math.Max(SettingsRows.ActualHeight + SettingsRows.Margin.Top + SettingsRows.Margin.Bottom - SettingsView.Height, 0);

    void SettingsRows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        _settingsScroll.Target = Math.Clamp(_settingsScroll.Target, 0, SettingsOverflow);
        StartShapeLoop();
    }

    void ScrollSettings(int notches)
    {
        _settingsScroll.Target = Math.Clamp(_settingsScroll.Target + notches * SettingsWheelStep, 0, SettingsOverflow);
        StartShapeLoop();
    }

    void ShrinkSettingsEdges(double scrolled)
    {
        double top = Math.Clamp(scrolled / SettingsEdgeZone, 0, 1), bottom = Math.Clamp((SettingsOverflow - scrolled) / SettingsEdgeZone, 0, 1);
        foreach (FrameworkElement row in SettingsRows.Children)
        {
            double middle = VisualTreeHelper.GetOffset(row).Y + SettingsRows.Margin.Top + row.ActualHeight / 2 - scrolled;
            double fromTop = Math.Clamp(middle / SettingsEdgeZone, 0, 1), fromBottom = Math.Clamp((SettingsView.Height - middle) / SettingsEdgeZone, 0, 1);
            double shrink = Math.Max(top * Smooth(1 - fromTop), bottom * Smooth(1 - fromBottom));

            if (row.RenderTransform is not ScaleTransform size)
            {
                row.RenderTransform = size = new ScaleTransform();
                row.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            size.ScaleX = size.ScaleY = 1 - (1 - SettingsEdgeScale) * shrink;
        }
    }

    void ApplySettingsScroll()
    {
        double scrolled = _settingsScroll.Value;
        SettingsEdgeTop.Color = EdgeFade(scrolled);
        SettingsEdgeBottom.Color = EdgeFade(SettingsOverflow - scrolled);
        if (SettingsView.IsVisible) ShrinkSettingsEdges(scrolled);
        if (SettingsMove.Y == -scrolled) return;

        SettingsMove.Y = -scrolled;
        if (_view == View.Settings) SettingsRows.FollowPointer();
    }

    void Accent_Click(object sender, RoutedEventArgs e)
    {
        Settings.Accent = ((RadioButton)sender).Background is SolidColorBrush picked ? picked.Color : null;
        RefreshLookPage();
        SyncAccent();
        SyncRim();
    }

    void RefreshLookPage()
    {
        SizeText.Text = Settings.Scale + "%";
        GapText.Text = Settings.Gap + " px";
        SizeSlider.Set(Settings.Scale, LookView.IsVisible);
        GapSlider.Set(Settings.Gap, LookView.IsVisible);
        PillOpacityText.Text = Settings.PillOpacity + "%";
        OpenOpacityText.Text = Settings.OpenOpacity + "%";
        PillOpacitySlider.Set(Settings.PillOpacity, LookView.IsVisible);
        OpenOpacitySlider.Set(Settings.OpenOpacity, LookView.IsVisible);
        DotsSegments.Set(Settings.Dots ? 1 : 0, LookView.IsVisible);
        SeekHoverSegments.Set((int)Settings.SeekHover, LookView.IsVisible);
        GlassSwitch.Set(Settings.Glass, LookView.IsVisible);
        ((RadioButton)BackdropStrip.Children[(int)Settings.Backdrop]).IsChecked = true;
        ((RadioButton)HoverStrip.Children[(int)Settings.Hover]).IsChecked = true;
        BackdropText.Text = Settings.Backdrop switch
        {
            Backdrop.Matrix => "Матрица",
            Backdrop.Stars => "Звёзды",
            Backdrop.MatrixAndStars => "Матрица и звёзды",
            _ => "Свечение",
        };
        HoverText.Text = Settings.Hover switch
        {
            Hover.Magnet => "Магнит",
            Hover.Flow => "Перетекание",
            _ => "Диск",
        };
        foreach (RadioButton dot in AccentStrip.Children)
        {
            Color? color = dot.Background is SolidColorBrush own ? own.Color : null;
            if (color != Settings.Accent) continue;
            dot.IsChecked = true;
            AccentText.Text = (string)dot.Tag;
        }
    }

    void ApplyLook()
    {
        RefreshLookPage();
        _userScale.Target = Settings.Scale / 100.0;
        _topGap.Target = Settings.Gap;
        UpdateTargets();
    }
}
