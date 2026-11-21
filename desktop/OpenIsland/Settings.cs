using System.Windows.Media;
using Microsoft.Win32;

namespace OpenIsland;

[Flags]
enum Backdrop
{
    Glow = 0,
    Matrix = 1,
    Stars = 2,
    MatrixAndStars = Matrix | Stars,
}

enum Hover
{
    Disc,
    Magnet,
    Flow,
}

enum SeekHover
{
    Magnifier,
    Wave,
}

public enum ThemeMode
{
    Dark = 0,
    Light = 1,
    System = 2,
}

public enum ThemePreset
{
    Default = 0,
    Oled = 1,
    Nord = 2,
    Cyberpunk = 3,
}

static class Settings
{
    const string Key = @"Software\OpenIsland";
    const string LegacyKey = @"Software\DynamicIsland";
    const int MinScale = 80, MaxScale = 200, MaxGap = 24;
    const int DefaultScale = 100, DefaultGap = 8;
    const int MinOpacity = 30, MaxOpacity = 100, DefaultOpacity = 100;
    const int MinNoticeSeconds = 2, MaxNoticeSeconds = 10, DefaultNoticeSeconds = 4;

    static bool _migrated;

    static bool _network = ReadSwitch(nameof(Network)), _hideFullscreen = ReadSwitch(nameof(HideFullscreen));
    static bool _rim = ReadSwitch(nameof(Rim)), _appVolume = ReadSwitch(nameof(AppVolume));
    static bool _taskToast = ReadSwitch(nameof(TaskToast)), _taskChime = ReadSwitch(nameof(TaskChime));
    static bool _clipboard = ReadSwitch(nameof(Clipboard));
    static bool _uiSoundFx = ReadSwitch(nameof(UiSoundFx), true);
    static bool _clickThrough = ReadSwitch(nameof(ClickThrough), false);
    static bool _hideFromCapture = ReadSwitch(nameof(HideFromCapture), false);
    static string _weatherCity = Read(nameof(WeatherCity), "");
    static double _weatherLat = ReadDouble(nameof(WeatherLat), 0.0);
    static double _weatherLon = ReadDouble(nameof(WeatherLon), 0.0);
    static string _quickLaunch = Read(nameof(QuickLaunch), "");
    static string _fullscreenExceptions = Read(nameof(FullscreenExceptions), "");
    static int _pillOpacity = Math.Clamp(Read(nameof(PillOpacity), DefaultOpacity), MinOpacity, MaxOpacity);
    static int _openOpacity = Math.Clamp(Read(nameof(OpenOpacity), DefaultOpacity), MinOpacity, MaxOpacity);
    static bool _dots = ReadSwitch(nameof(Dots), false);
    static bool _glass = ReadSwitch(nameof(Glass), false);
    static Backdrop _backdrop = (Backdrop)Math.Clamp(Read(nameof(Backdrop), 0), 0, (int)Backdrop.MatrixAndStars);
    static Hover _hover = (Hover)Math.Clamp(Read(nameof(Hover), 0), 0, (int)Hover.Flow);
    static SeekHover _seekHover = (SeekHover)Math.Clamp(Read(nameof(SeekHover), 0), 0, (int)SeekHover.Wave);
    static int _scale = Math.Clamp(Read(nameof(Scale), DefaultScale), MinScale, MaxScale);
    static int _gap = Math.Clamp(Read(nameof(Gap), DefaultGap), 0, MaxGap);
    static int _noticeSeconds = Math.Clamp(Read(nameof(NoticeSeconds), DefaultNoticeSeconds), MinNoticeSeconds, MaxNoticeSeconds);
    static int _accent = Read(nameof(Accent), 0);
    static ThemeMode _themeMode = (ThemeMode)Math.Clamp(Read(nameof(ThemeMode), (int)ThemeMode.Dark), 0, (int)ThemeMode.System);
    static ThemePreset _themePreset = (ThemePreset)Math.Clamp(Read(nameof(ThemePreset), (int)ThemePreset.Default), 0, (int)ThemePreset.Cyberpunk);

    public static ThemeMode ThemeMode
    {
        get => _themeMode;
        set => Write(nameof(ThemeMode), (int)(_themeMode = value));
    }

    public static ThemePreset ThemePreset
    {
        get => _themePreset;
        set => Write(nameof(ThemePreset), (int)(_themePreset = value));
    }

    public static bool Dots
    {
        get => _dots;
        set => Write(nameof(Dots), _dots = value);
    }

    public static bool Glass
    {
        get => _glass;
        set => Write(nameof(Glass), _glass = value);
    }

    public static Backdrop Backdrop
    {
        get => _backdrop;
        set => Write(nameof(Backdrop), (int)(_backdrop = value));
    }

    public static Hover Hover
    {
        get => _hover;
        set => Write(nameof(Hover), (int)(_hover = value));
    }

    public static SeekHover SeekHover
    {
        get => _seekHover;
        set => Write(nameof(SeekHover), (int)(_seekHover = value));
    }

    public static bool Rim
    {
        get => _rim;
        set => Write(nameof(Rim), _rim = value);
    }

    public static bool AppVolume
    {
        get => _appVolume;
        set => Write(nameof(AppVolume), _appVolume = value);
    }

    public static bool Network
    {
        get => _network;
        set => Write(nameof(Network), _network = value);
    }

    public static bool HideFullscreen
    {
        get => _hideFullscreen;
        set => Write(nameof(HideFullscreen), _hideFullscreen = value);
    }

    /// <summary>Список процессов-исключений через запятую, для которых островок не скрывается в полноэкранном режиме.</summary>
    public static string FullscreenExceptions
    {
        get => _fullscreenExceptions;
        set => Write(nameof(FullscreenExceptions), _fullscreenExceptions = value ?? "");
    }

    /// <summary>Проверяет, входит ли имя процесса в список исключений скрытия на полном экране.</summary>
    public static bool IsFullscreenExcluded(string? processName)
    {
        if (string.IsNullOrWhiteSpace(_fullscreenExceptions) || string.IsNullOrWhiteSpace(processName)) return false;
        string target = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        string[] parts = _fullscreenExceptions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string part in parts)
        {
            string cleanPart = part.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? part[..^4]
                : part;
            if (string.Equals(cleanPart, target, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static bool TaskToast
    {
        get => _taskToast;
        set => Write(nameof(TaskToast), _taskToast = value);
    }

    /// <summary>Звук завершения задачи агента (независим от тоста).</summary>
    public static bool TaskChime
    {
        get => _taskChime;
        set => Write(nameof(TaskChime), _taskChime = value);
    }

    /// <summary>Вести историю буфера обмена (только в памяти, ничего не сохраняется на диск).</summary>
    public static bool Clipboard
    {
        get => _clipboard;
        set => Write(nameof(Clipboard), _clipboard = value);
    }

    /// <summary>Звуковые микро-эффекты интерфейса (щелчки переключателей, поп раскрытия, тык сворачивания).</summary>
    public static bool UiSoundFx
    {
        get => _uiSoundFx;
        set => Write(nameof(UiSoundFx), _uiSoundFx = value);
    }

    /// <summary>Вручную заданный город для погоды (пусто, если автоопределение по IP).</summary>
    public static string WeatherCity
    {
        get => _weatherCity;
        set => Write(nameof(WeatherCity), _weatherCity = value ?? "");
    }

    public static double WeatherLat
    {
        get => _weatherLat;
        set => Write(nameof(WeatherLat), _weatherLat = value);
    }

    public static double WeatherLon
    {
        get => _weatherLon;
        set => Write(nameof(WeatherLon), _weatherLon = value);
    }

    public static bool HasManualWeatherLocation =>
        !string.IsNullOrWhiteSpace(_weatherCity) && (Math.Abs(_weatherLat) > 0.0001 || Math.Abs(_weatherLon) > 0.0001);

    public static void ResetWeatherLocation()
    {
        WeatherCity = "";
        WeatherLat = 0.0;
        WeatherLon = 0.0;
    }

    /// <summary>Скрывать остров на записях экрана, стримах и скриншотах (WDA_EXCLUDEFROMCAPTURE).</summary>
    public static bool HideFromCapture
    {
        get => _hideFromCapture;
        set => Write(nameof(HideFromCapture), _hideFromCapture = value);
    }

    /// <summary>Сквозной режим: остров не ловит мышь, клики уходят под него. Выключается хоткеем Ctrl+Alt+I или из меню трея.</summary>
    public static bool ClickThrough
    {
        get => _clickThrough;
        set => Write(nameof(ClickThrough), _clickThrough = value);
    }

    /// <summary>Свои программы карточки «Быстрый запуск»: по строке «Имя|Путь|Аргументы» на команду.</summary>
    public static string QuickLaunch
    {
        get => _quickLaunch;
        set => Write(nameof(QuickLaunch), _quickLaunch = value ?? "");
    }

    public static int Scale
    {
        get => _scale;
        set => Write(nameof(Scale), _scale = Math.Clamp(value, MinScale, MaxScale));
    }

    /// <summary>Непрозрачность свёрнутой пилюли, % (30–100).</summary>
    public static int PillOpacity
    {
        get => _pillOpacity;
        set => Write(nameof(PillOpacity), _pillOpacity = Math.Clamp(value, MinOpacity, MaxOpacity));
    }

    /// <summary>Непрозрачность раскрытой карточки, % (30–100).</summary>
    public static int OpenOpacity
    {
        get => _openOpacity;
        set => Write(nameof(OpenOpacity), _openOpacity = Math.Clamp(value, MinOpacity, MaxOpacity));
    }

    public static int Gap
    {
        get => _gap;
        set => Write(nameof(Gap), _gap = Math.Clamp(value, 0, MaxGap));
    }

    /// <summary>Время жизни событийной карточки, секунды (2–10). Пресеты — <see cref="NoticePresets"/>.</summary>
    public static int NoticeSeconds
    {
        get => _noticeSeconds;
        set => Write(nameof(NoticeSeconds), _noticeSeconds = Math.Clamp(value, MinNoticeSeconds, MaxNoticeSeconds));
    }

    public static IReadOnlyList<int> NoticePresets { get; } = [2, 3, 4, 6, 8, 10];

    /// <summary>Следующий пресет по кругу: 2 → 3 → 4 → 6 → 8 → 10 → 2.</summary>
    public static int NextNoticeSeconds(int current)
    {
        foreach (int preset in NoticePresets)
            if (preset > current) return preset;
        return NoticePresets[0];
    }

    public static Color? Accent
    {
        get => _accent == 0 ? null : Color.FromRgb((byte)(_accent >> 16), (byte)(_accent >> 8), (byte)_accent);
        set => Write(nameof(Accent), _accent = value is { } c ? c.R << 16 | c.G << 8 | c.B : 0);
    }

    static bool ReadSwitch(string name, bool fallback = true) => Read(name, fallback ? 1 : 0) != 0;

    static double ReadDouble(string name, double fallback)
    {
        string raw = Read(name, "");
        return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : fallback;
    }

    static T Read<T>(string name, T fallback)
    {
        MigrateLegacy();
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is T value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    static void MigrateLegacy()
    {
        if (_migrated) return;
        _migrated = true;
        try
        {
            using RegistryKey? legacy = Registry.CurrentUser.OpenSubKey(LegacyKey);
            if (legacy is null || legacy.ValueCount == 0) return;

            using RegistryKey target = Registry.CurrentUser.CreateSubKey(Key);
            if (target.ValueCount > 0) return;

            foreach (string valueName in legacy.GetValueNames())
            {
                if (legacy.GetValue(valueName) is { } value)
                    target.SetValue(valueName, value, legacy.GetValueKind(valueName));
            }
            App.LogDebug($"Settings: migrated {legacy.ValueCount} values from {LegacyKey} to {Key}.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    static void Write(string name, bool value) => Write(name, value ? 1 : 0);

    static void Write(string name, int value) => Write(name, value, RegistryValueKind.DWord);

    static void Write(string name, string value) => Write(name, value, RegistryValueKind.String);

    static void Write(string name, double value) => Write(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    static void Write(string name, object value, RegistryValueKind kind)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(name, value, kind);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
