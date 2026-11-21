using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using static OpenIsland.Motion;

namespace OpenIsland;

public partial class MainWindow : Window
{
    enum View { Idle, Sound, Media, Timer, Volume, Charge, Headset, Focus, Toast, Notice, MediaBig, IdleBig, TimerBig, TimerSet, Menu, Settings, Look, Update, Loading, OpenCode, OpenCodeBig, Perf, Clipboard, Processes, QuickLaunch, Weather, Shelf }

    enum Panel { None, Player, Timer, TimerSet, Menu, Settings, Look, Update, OpenCode, Perf, Clipboard, Processes, QuickLaunch, Weather, Shelf }

    readonly record struct PillShape(double Width, double Height, double Radius);

    static readonly Dictionary<View, PillShape> PillShapes = new()
    {
        [View.Idle] = new(118, 34, 17),
        [View.Sound] = new(110, 34, 17),
        [View.Media] = new(210, 34, 17),
        [View.Timer] = new(132, 34, 17),
        [View.Volume] = new(250, 34, 17),
        [View.Charge] = new(230, 34, 17),
        [View.Headset] = new(320, 64, 29),
        [View.Focus] = new(236, 34, 17),
        [View.Toast] = new(340, 68, 30),
        [View.Notice] = new(320, 64, 29),
        [View.MediaBig] = new(380, PlayerHeight, 40),
        [View.IdleBig] = new(320, 124, 38),
        [View.TimerBig] = new(330, 92, 40),
        [View.TimerSet] = new(300, 190, 38),
        [View.Menu] = new(300, 544, 34),
        [View.Settings] = new(320, 496, 34),
        [View.Look] = new(352, LookHeight, 34),
        [View.Update] = new(320, 150, 34),
        [View.Loading] = new(118, 34, 17),
        [View.OpenCode] = new(238, 34, 17),
        [View.OpenCodeBig] = new(380, 486, 36),
        [View.Perf] = new(340, 410, 34),
        [View.Clipboard] = new(340, 430, 34),
        [View.Processes] = new(340, 470, 34),
        [View.QuickLaunch] = new(340, 430, 34),
        [View.Weather] = new(340, 430, 34),
        [View.Shelf] = new(340, 430, 34),
    };

    readonly record struct MediaSpot(
        double CoverLeft, double CoverTop, double CoverSize,
        double BarsRight, double BarsCenterY, double BarsWidth, double BarsHeight, double BarsOpen, bool Cover)
    {
        public double[] Values => [CoverLeft, CoverTop, CoverSize, BarsRight, BarsCenterY, BarsWidth, BarsHeight, BarsOpen];
    }

    static readonly Dictionary<View, MediaSpot> MediaSpots = new()
    {
        [View.Sound] = new(0, 0, 0, 38, 17, 34, 18, 1, false),
        [View.Media] = new(7, 6, 22, 13, 17, 21, 16, 0, true),
        [View.OpenCode] = new(0, 0, 0, 13, 17, 21, 16, 0, false),
        [View.Toast] = new(12, 12, 44, 20, 34, 23, 20, 0, true),
        [View.MediaBig] = new(20, 20, 64, 22, 52, 38, 26, 1, true),
    };

    static readonly View[] MenuPages = [View.Settings, View.Look, View.TimerSet, View.TimerBig, View.OpenCodeBig, View.Perf, View.Clipboard, View.Processes, View.QuickLaunch, View.Weather, View.Shelf];

    static readonly View[] SettingsPages = [View.Update];

    const double HostWidth = 620, HostHeight = 592;
    const double CompactMaxHeight = 40;
    const double BarelyVisible = 0.05;
    const double SoundPeak = 0.004, SoundHoldSeconds = 2.2;
    const double IntroScale = 0.3, IntroOffset = -50;
    const int FullscreenCheckTicks = 5, ClockUpdateTicks = 10, HeadsetReadTicks = 300;
    static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    static readonly TimeSpan PausedMediaLinger = TimeSpan.FromSeconds(30);
    static readonly TimeSpan CollapseDelay = TimeSpan.FromMilliseconds(550);
    static readonly TimeSpan LongCollapseDelay = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan AwayDuration = TimeSpan.FromSeconds(5);

    readonly Dictionary<View, FrameworkElement> _views;
    readonly SolidColorBrush _dim, _orange, _green, _red, _indigo;
    readonly AudioService _audio = new();
    readonly MediaService _media;
    readonly NetworkService _network;
    readonly Updater _updater = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _ticker = new() { Interval = TickInterval };
    readonly DelayedAction _transientTimeout, _collapseTimeout, _awayTimeout;
    readonly View? _forcedView;
    readonly double _forcedTimerSeconds;
    readonly int _forcedHeadsetCharge;

    View _view = View.Idle;
    View? _transientView;
    Panel _panel;
    bool _hovered, _pressed, _hiddenByFullscreen, _sentAway;
    bool _activatable;
    bool _sounding;
    double _soundingUntil;
    IntPtr _typedAfter;
    IntPtr _hwnd;
    int _ticks;

    const int OpenCodeHotkeyId = 0x4F49, ClickThroughHotkeyId = 0x4F4A, ColorPickerHotkeyId = 0x4F4B, OcrHotkeyId = 0x4F4C;
    HwndSource? _windowSource;
    bool _hotkeyRegistered;
    bool _clickThroughHotkeyRegistered;
    bool _colorPickerHotkeyRegistered;
    bool _ocrHotkeyRegistered;
    bool _clickThrough;

    public MainWindow()
    {
        InitializeComponent();
        Width *= LargestScale / 100.0;
        Height = Height * LargestScale / 100.0 + LargestGap;

        _dim = BrushResource("Dim");
        _orange = BrushResource("Orange");
        _green = BrushResource("Green");
        _red = BrushResource("Red");
        _indigo = BrushResource("Indigo");

        _views = new()
        {
            [View.Idle] = IdleView,
            [View.Sound] = SoundView,
            [View.Media] = MediaView,
            [View.Timer] = TimerView,
            [View.Volume] = VolumeView,
            [View.Charge] = ChargeView,
            [View.Headset] = HeadsetView,
            [View.Focus] = FocusView,
            [View.Toast] = ToastView,
            [View.Notice] = NoticeView,
            [View.MediaBig] = MediaBigView,
            [View.IdleBig] = IdleBigView,
            [View.TimerBig] = TimerBigView,
            [View.TimerSet] = TimerSetView,
            [View.Menu] = MenuView,
            [View.Settings] = SettingsView,
            [View.Look] = LookView,
            [View.Update] = UpdatePage,
            [View.Loading] = LoadingView,
            [View.OpenCode] = OpenCodeView,
            [View.OpenCodeBig] = OpenCodeBigView,
            [View.Perf] = PerfView,
            [View.Clipboard] = ClipboardView,
            [View.Processes] = ProcessesView,
            [View.QuickLaunch] = QuickLaunchView,
            [View.Weather] = WeatherView,
            [View.Shelf] = ShelfView,
        };
        _spotSprings = [_coverLeft, _coverTop, _coverSize, _barsRight, _barsCenterY, _barsWidth, _barsHeight, _barsOpen];
        _springs =
        [
            _width, _height, _radius, _scale, _offsetY, _userScale, _topGap, _opacity, _bubbleSplit, _bubbleScale, _bubbleTimer,
            _lookScroll, _updateNotesScroll, _volumeOvershoot, _leanX, _coverScale, _coverSnapX, _coverSnapY, .. _spotSprings,
        ];
        _timerTint = new SolidColorBrush(_orange.Color);

        _forcedView = Enum.TryParse(Argument("--view"), true, out View forced) ? forced : null;
        _forcedTimerSeconds = double.TryParse(Argument("--timer"), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) ? seconds : 0;
        _forcedHeadsetCharge = int.TryParse(Argument("--headset"), out int charge) ? Math.Clamp(charge, 0, 100) : Headset.Unknown;

        _shapeLoop = new FrameLoop(AdvanceShape);
        _glass = new Glass(this);
        _eqLoop = new FrameLoop(AdvanceEq, EqFrameSeconds);
        _seekLoop = new FrameLoop(AdvanceSeek);
        _transientTimeout = new DelayedAction(EndTransient);
        _collapseTimeout = new DelayedAction(Collapse);
        _awayTimeout = new DelayedAction(ReturnFromAway);
        _overshootTimeout = new DelayedAction(ReleaseVolumeOvershoot);

        _media = new MediaService(Dispatcher);
        _media.Changed += OnMediaChanged;
        _network = new NetworkService(Dispatcher);
        _network.Changed += OnNetworkChanged;
        _perf = new PerfService(Dispatcher);
        _perf.Changed += OnPerfChanged;
        _devProcesses = new DevProcessService(Dispatcher);
        _updater.Changed += RefreshUpdatePage;

        PrepareViews();
        InitOpenCode();
        InitClipboard();
        InitPerf();
        InitProcesses();
        InitQuickLaunch();
        InitWeather();
        InitFileShelf();
        ApplyTimerTint();
        TuneDragSprings(false);

        AllowDrop = true;
        DragEnter += MainWindow_DragEnter;
        DragOver += MainWindow_DragOver;
        Drop += MainWindow_Drop;

        _ticker.Tick += (_, _) => OnTick();
        IsVisibleChanged += (_, _) => SyncEq();
        Loaded += OnLoaded;
        Deactivated += OnDeactivated;
    }

    void PrepareViews()
    {
        foreach (FrameworkElement view in _views.Values)
        {
            view.RenderTransformOrigin = new Point(0.5, 0.5);
            view.RenderTransform = CreateViewTransforms();
            view.Visibility = Visibility.Collapsed;
            view.Opacity = 0;
        }
        IdleView.Visibility = Visibility.Visible;
        IdleView.Opacity = 1;

        Shared.RenderTransform = CreateViewTransforms();
        ArtSpot.Clip = Squircle.Of(new Rect(0, 0, FullCoverSize, FullCoverSize), CoverRadius);
        Eq.Fill = _accentBrush;
        Eq.Dots = Settings.Dots;
        PlayerMatrix.Rounding = PillShapes[View.MediaBig].Radius;
        SyncBackdrop();
        foreach (FrameworkElement icon in new FrameworkElement[] { PlayIcon, PauseIcon, TimerPlayIcon, TimerPauseIcon })
        {
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = new ScaleTransform(1, 1);
        }
    }

    bool IsMediaActive => _media.HasTrack && (_media.IsPlaying || DateTime.UtcNow - _lastPlayedAt < PausedMediaLinger);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.HideFromTaskSwitcher(_hwnd);
        // Оверлей не должен забирать фокус, пока не открыта панель ввода.
        Native.SetActivatable(_hwnd, false);
        _activatable = false;
        if (Native.RegisterShellHook(_hwnd)) HwndSource.FromHwnd(_hwnd).AddHook(OnShellMessage);

        _windowSource = HwndSource.FromHwnd(_hwnd);
        if (_windowSource != null)
        {
            _windowSource.AddHook(OnHotkeyMessage);
            _windowSource.AddHook(OnClipboardMessage);
            Native.WatchClipboard(_hwnd, true);
            _hotkeyRegistered = Native.RegisterOpenCodeHotkey(_hwnd, OpenCodeHotkeyId);
            App.LogDebug(_hotkeyRegistered
                ? "Global hotkey Ctrl+Alt+O registered."
                : "Global hotkey Ctrl+Alt+O is unavailable (already in use).");
            _clickThroughHotkeyRegistered = Native.RegisterClickThroughHotkey(_hwnd, ClickThroughHotkeyId);
            App.LogDebug(_clickThroughHotkeyRegistered
                ? "Global hotkey Ctrl+Alt+I registered."
                : "Global hotkey Ctrl+Alt+I is unavailable (already in use).");
            _colorPickerHotkeyRegistered = Native.RegisterColorPickerHotkey(_hwnd, ColorPickerHotkeyId);
            App.LogDebug(_colorPickerHotkeyRegistered
                ? "Global hotkey Ctrl+Alt+C registered."
                : "Global hotkey Ctrl+Alt+C is unavailable (already in use).");
            _ocrHotkeyRegistered = Native.RegisterOcrHotkey(_hwnd, OcrHotkeyId);
            App.LogDebug(_ocrHotkeyRegistered
                ? "Global hotkey Ctrl+Alt+X registered."
                : "Global hotkey Ctrl+Alt+X is unavailable (already in use).");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            Native.UnregisterOpenCodeHotkey(_hwnd, OpenCodeHotkeyId);
            _hotkeyRegistered = false;
        }
        if (_clickThroughHotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            Native.UnregisterHotkey(_hwnd, ClickThroughHotkeyId);
            _clickThroughHotkeyRegistered = false;
        }
        if (_colorPickerHotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            Native.UnregisterHotkey(_hwnd, ColorPickerHotkeyId);
            _colorPickerHotkeyRegistered = false;
        }
        if (_ocrHotkeyRegistered && _hwnd != IntPtr.Zero)
        {
            Native.UnregisterHotkey(_hwnd, OcrHotkeyId);
            _ocrHotkeyRegistered = false;
        }
        _windowSource?.RemoveHook(OnHotkeyMessage);
        _windowSource?.RemoveHook(OnClipboardMessage);
        if (_hwnd != IntPtr.Zero) Native.WatchClipboard(_hwnd, false);
        _openCodeReady = false;
        _openCode.Dispose();
        _perf.Dispose();
        _devProcesses.Dispose();
        _weather.Dispose();
        DisposeTray();
        base.OnClosed(e);
    }

    IntPtr OnHotkeyMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (Native.IsHotkeyMessage(msg, wParam, OpenCodeHotkeyId))
        {
            handled = true;
            ToggleOpenCodeFromHotkey();
            return IntPtr.Zero;
        }

        if (Native.IsHotkeyMessage(msg, wParam, ClickThroughHotkeyId))
        {
            handled = true;
            ToggleClickThrough();
            return IntPtr.Zero;
        }

        if (Native.IsHotkeyMessage(msg, wParam, ColorPickerHotkeyId))
        {
            handled = true;
            StartColorPicker();
            return IntPtr.Zero;
        }

        if (Native.IsHotkeyMessage(msg, wParam, OcrHotkeyId))
        {
            handled = true;
            StartScreenOcr();
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    public void StartColorPicker()
    {
        if (ColorPickerWindow.IsOpen) return;
        ColorPickerWindow.Start((color, hex, rgb) =>
        {
            Dispatcher.Invoke(() =>
            {
                Notify(Glyph.Drop, new SolidColorBrush(color), "Цвет скопирован", $"{hex}  \u00B7  {rgb}", seconds: 4);
            });
        });
    }

    public void StartScreenOcr()
    {
        if (ScreenOcrWindow.IsOpen) return;
        ScreenOcrWindow.Start(
            (text, preview) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Notify(Glyph.PointerText, _green, "Текст скопирован", preview, seconds: 4);
                });
            },
            () =>
            {
                Dispatcher.Invoke(() =>
                {
                    Notify(Glyph.PointerText, _orange, "Текст не найден", "В выбранной области нет распознаваемого текста", seconds: 3);
                });
            });
    }

    void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            {
                int added = FileShelf.AddFiles(files);
                if (added > 0)
                {
                    ShowPanel(Panel.Shelf);
                    Notify(Glyph.Folder, _indigo, "Файлы на полке", $"{added} шт. добавлено", seconds: 3);
                }
            }
        }
    }

    void ToggleClickThrough()
    {
        bool on = !_clickThrough;
        Settings.ClickThrough = on;
        ApplyClickThrough(on);
        Notify(Glyph.Pointer, on ? _indigo : _dim, "Сквозной режим", on ? "Клики проходят сквозь остров" : "Остров снова ловит клики", force: true);
    }

    /// <summary>Сквозной режим: остров перестаёт быть интерактивным, пока не выключить его хоткеем или из трея.</summary>
    void ApplyClickThrough(bool on)
    {
        if (_clickThrough == on) return;
        _clickThrough = on;
        if (_hwnd != IntPtr.Zero) Native.SetClickThrough(_hwnd, on);
        if (on)
        {
            // Мышь больше не приходит в окно: сами снимаем наведение и захват,
            // иначе пилюля останется раскрытой под неподвижным курсором.
            _hovered = false;
            CancelGrab();
            CollapseNow();
            UpdateTargets();
        }
        App.LogDebug(on ? "Click-through mode enabled." : "Click-through mode disabled.");
    }

    /// <summary>Скрытие окна из захвата экрана (OBS, Discord, Zoom, скриншоты).</summary>
    void ApplyHideFromCapture()
    {
        bool on = Settings.HideFromCapture;
        if (_hwnd != IntPtr.Zero) Native.SetExcludeFromCapture(_hwnd, on);
        if (_glass.Handle != IntPtr.Zero) Native.SetExcludeFromCapture(_glass.Handle, on);
        App.LogDebug(on ? "ExcludeFromCapture enabled." : "ExcludeFromCapture disabled.");
    }

    void ToggleOpenCodeFromHotkey()
    {
        if (_panel == Panel.OpenCode)
        {
            CollapseNow();
            return;
        }

        ShowPanel(Panel.OpenCode);
        Dispatcher.BeginInvoke(() => OpenCodePromptBox.Focus(), DispatcherPriority.Input);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyShape();
    }

    static string? Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    static bool Switch(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

    SolidColorBrush BrushResource(string key) => (SolidColorBrush)FindResource(key);

    IntPtr OnShellMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (Native.MediaKeyOf(msg, wParam, lParam))
        {
            case Native.MediaKey.VolumeUp when IsVolumeAtLimit(true):
                OvershootVolume(true);
                break;
            case Native.MediaKey.VolumeDown when IsVolumeAtLimit(false):
                OvershootVolume(false);
                break;
            case Native.MediaKey.NextTrack:
                RememberSkip(1);
                NextIcon.Play();
                break;
            case Native.MediaKey.PreviousTrack:
                RememberSkip(-1);
                PrevIcon.Play();
                break;
        }
        return IntPtr.Zero;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.LogDebug("MainWindow.OnLoaded started.");

        CenterOnScreen();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(CenterOnScreen);
        UpdateClock();
        UpdateSwitches(false);
        RefreshLookPage();
        RefreshUpdatePage();
        SyncAccent(false);
        SyncGlass(false);
        PlayIntro();
        InitTray();
        ApplyClickThrough(Settings.ClickThrough || Switch("--clickthrough"));
        ApplyHideFromCapture();
        _ticker.Start();
        if (_forcedTimerSeconds > 0) StartTimer(TimeSpan.FromSeconds(_forcedTimerSeconds));
        if (_forcedView == View.Update) _ = _updater.CheckAsync();
        if (_forcedHeadsetCharge >= 0)
            ShowHeadset("AirPods Pro", Enum.TryParse(Argument("--gadget"), true, out Gadget.Model model) ? model : Gadget.Model.Earbuds,
                _forcedHeadsetCharge, _forcedHeadsetCharge <= HeadsetLowPercent);

        try { await _media.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        try { await _network.StartAsync(); }
        catch (Exception ex) { App.Log(ex); }
        App.LogDebug("MainWindow.OnLoaded finished successfully.");
    }

    void CenterOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        _glass.Place();
    }

    void PlayIntro()
    {
        _scale.Value = IntroScale;
        _offsetY.Value = IntroOffset;
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Ms(260)));
        UpdateView();
        UpdateTargets();
    }

    void Exit()
    {
        App.LogDebug("MainWindow.Exit() called! StackTrace: " + Environment.StackTrace);
        _ticker.Stop();
        _alarm.Stop();
        _glass.Hide();
        var fade = new DoubleAnimation(0, Ms(220));
        fade.Completed += (_, _) => Application.Current.Shutdown();
        Root.BeginAnimation(OpacityProperty, fade);
        _scale.Target = _bubbleScale.Target = 0.5;
        StartShapeLoop();
    }

    static void Safely(Action step)
    {
        try
        {
            step();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void OnTick()
    {
        _ticks++;
        Safely(PollVolume);
        Safely(PollSound);
        if (_ticks % 2 == 0) Safely(RefreshHover);
        if (_ticks % HeadsetReadTicks == 1) Safely(() => ReadHeadset(_audio.Device));
        Safely(UpdateTimer);
        if (_ticks % FullscreenCheckTicks == 0)
        {
            Safely(CheckFullscreen);
            Safely(PollDoNotDisturb);
        }
        if (_ticks % ClockUpdateTicks == 0)
        {
            Safely(UpdateClock);
            Safely(PollPower);
            Safely(StayOnTop);
            Safely(SyncAgentPill);
            if (_media.IsPlaying) _lastPlayedAt = DateTime.UtcNow;
            Safely(UpdateView);
        }
        if (_ticks % 2 == 0 && (_panel == Panel.OpenCode || _view == View.OpenCode)) Safely(UpdateOpenCodeTimer);
    }

    View TargetView()
    {
        View target = _forcedView ?? _panel switch
        {
            Panel.Menu => View.Menu,
            Panel.Settings => View.Settings,
            Panel.Look => View.Look,
            Panel.Update => View.Update,
            Panel.TimerSet => View.TimerSet,
            Panel.OpenCode => View.OpenCodeBig,
            Panel.Perf => View.Perf,
            Panel.Clipboard => View.Clipboard,
            Panel.Processes => View.Processes,
            Panel.QuickLaunch => View.QuickLaunch,
            Panel.Weather => View.Weather,
            Panel.Shelf => View.Shelf,
            Panel.Timer when _countdown.IsActive => View.TimerBig,
            Panel.Timer or Panel.Player => _media.HasTrack ? View.MediaBig : (_openCode.IsBusy ? View.OpenCodeBig : View.IdleBig),
            _ => _transientView ?? (_updater.State == Updater.Stage.Loading ? View.Loading
                : _countdown.IsActive ? View.Timer
                : _openCode.IsBusy ? View.OpenCode
                : IsMediaActive ? View.Media
                : _sounding ? View.Sound : View.Idle),
        };
        return target == View.Toast && !_media.HasTrack ? View.Idle : target;
    }

    void UpdateView()
    {
        View target = TargetView();
        bool changed = target != _view;
        _perf.SetActive(target == View.Perf, PerfView.IsMouseOver);
        _devProcesses.SetActive(target == View.Processes);
        if (target != View.Processes && _killPid != 0) ClearKill();
        if (changed) MorphTo(target);

        SyncEq();
        SyncRim();
        SyncCover();
        if (target == View.Clipboard) RefreshClipboardUI();
        if (!changed) return;
        if (target == View.MediaBig) StartSeekLoop();
    }

    void MorphTo(View target)
    {
        View previous = _view;
        PillShape from = ShapeOf(previous);
        _view = target;
        PillShape to = ShapeOf(target);
        bool growing = to.Width * to.Height >= from.Width * from.Height;
        _width.Tune(growing ? 330 : 260, growing ? 22 : 27);
        _height.Tune(growing ? 220 : 400, growing ? 22 : 33);
        foreach (Spring spring in _spotSprings) spring.Tune(_height.Stiffness, _height.Damping);

        _morphing = true;
        SwitchView(_views[target], MenuDirection(previous, target));
        MoveMediaSpot(previous, target, to);
        UpdateTargets();
    }

    void MoveMediaSpot(View from, View to, PillShape size)
    {
        bool had = MediaSpots.ContainsKey(from), has = MediaSpots.TryGetValue(to, out MediaSpot spot);
        if (has)
        {
            bool fresh = IsHidden(Shared);
            double[] places = spot.Values;
            for (int i = 0; i < _spotSprings.Length; i++)
            {
                if (fresh) _spotSprings[i].Snap(places[i]);
                else _spotSprings[i].Target = places[i];
            }
            if (fresh) ArtSpot.Opacity = spot.Cover ? 1 : 0;
            else ArtSpot.BeginAnimation(OpacityProperty, new DoubleAnimation(spot.Cover ? 1 : 0, Ms(200)));
        }
        ArtSpot.Cursor = to == View.MediaBig ? Cursors.Hand : null;
        if (has == had) return;

        ScaleTransform scale = FadeScaleOf(Shared);
        scale.CenterX = HostWidth / 2;
        scale.CenterY = (has ? size : ShapeOf(from)).Height / 2;
        if (has) FadeIn(Shared);
        else FadeOut(Shared);
    }

    static int MenuDirection(View from, View to) =>
        IsPageOf(from, to) ? 1 : IsPageOf(to, from) ? -1 : 0;

    static bool IsPageOf(View parent, View page) =>
        parent == View.Menu && MenuPages.Contains(page) || parent == View.Settings && SettingsPages.Contains(page);

    PillShape ShapeOf(View view) => view switch
    {
        View.Update => PillShapes[view] with { Height = UpdatePage.Height },
        View.Look => PillShapes[view] with { Height = LookView.Height },
        View.Processes => PillShapes[view] with { Height = ProcessesView.Height > 0 ? ProcessesView.Height : PillShapes[view].Height },
        _ => PillShapes[view],
    };

    bool ShowTransient(View view, double seconds, bool force = false)
    {
        if (force) _panel = Panel.None;
        else if (_panel != Panel.None || _hiddenByFullscreen || _sentAway) return false;
        _transientView = view;
        _transientTimeout.Start(TimeSpan.FromSeconds(seconds));
        UpdateView();
        return true;
    }

    void EndTransient()
    {
        _transientView = null;
        SilenceAlarm();
        // Следующая карточка из очереди показывается до общего обновления вида,
        // чтобы не мигнуть пустой пилюлей между событиями.
        ShowNextNotice();
        UpdateView();
    }

    void SelectPanel(Panel panel)
    {
        _panel = panel;
        _transientView = null;
        _transientTimeout.Cancel();
        ClearPendingNotices();
        SilenceAlarm();
    }

    void ShowPanel(Panel panel)
    {
        _panel = panel;
        ClearPendingNotices();
        if (panel == Panel.Menu)
        {
            SyncWeatherUI();
            SyncFileShelfUI();
        }
        if (panel == Panel.OpenCode) EnterTextEntry();
        else LeaveTextEntry();
        UpdateView();
        if (panel != Panel.None && !_hovered)
        {
            _collapseTimeout.Start(panel == Panel.OpenCode ? LongCollapseDelay : CollapseDelay);
        }
    }

    /// <summary>Готовит окно к вводу текста: запоминает активное окно, разрешает активацию, забирает фокус.</summary>
    void EnterTextEntry()
    {
        if (_typedAfter == IntPtr.Zero)
        {
            IntPtr foreground = Native.ForegroundWindow;
            if (foreground != _hwnd) _typedAfter = foreground;
        }
        MakeActivatable(true);
        Native.BringForward(_hwnd);
        Activate();
    }

    /// <summary>Возвращает фокус прежнему окну и запрещает активацию оверлея.</summary>
    void LeaveTextEntry()
    {
        if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();
        if (IsActive && _typedAfter != IntPtr.Zero) Native.BringForward(_typedAfter);
        _typedAfter = IntPtr.Zero;
        MakeActivatable(false);
    }

    void MakeActivatable(bool on)
    {
        if (_activatable == on) return;
        _activatable = on;
        Native.SetActivatable(_hwnd, on);
    }

    void Collapse()
    {
        if (_hovered) return;
        if (_activatable)
        {
            // Пользователь набирает текст — не мешаем.
            _collapseTimeout.Start(CollapseDelay);
            return;
        }
        ShowPanel(Panel.None);
    }

    /// <summary>Сворачивает островок, когда окно теряет активацию (клик в другое приложение).</summary>
    void OnDeactivated(object? sender, EventArgs e)
    {
        if (_panel != Panel.None && !_hovered && !_quickLaunchDialog) CollapseNow();
    }

    /// <summary>Немедленно вернуть островок в компактную таблетку (клик вне окна, горячая клавиша).</summary>
    void CollapseNow()
    {
        _collapseTimeout.Cancel();
        if (_panel != Panel.None) ShowPanel(Panel.None);
    }

    /// <summary>Пере-проверяет наведение курсора, когда островок сдвинулся под неподвижной мышью.</summary>
    void RefreshHover()
    {
        // В сквозном режиме окно не получает мышь, и IsMouseOver «залипает» на последнем значении.
        bool over = !_clickThrough && Island.IsMouseOver;
        if (over == _hovered) return;
        _hovered = over;
        if (over) _collapseTimeout.Cancel();
        else if (_panel != Panel.None) _collapseTimeout.Start(CollapseDelay);
        UpdateTargets();
    }

    void ReturnFromAway()
    {
        _sentAway = false;
        UpdateTargets();
        SyncEq();
    }
}
