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

static class Settings
{
    const string Key = @"Software\OpenIsland";
    const string LegacyKey = @"Software\DynamicIsland";
    const int MinScale = 85, MaxScale = 130, MaxGap = 24;
    const int DefaultScale = 100, DefaultGap = 8;

    static bool _migrated;

    static bool _network = ReadSwitch(nameof(Network)), _hideFullscreen = ReadSwitch(nameof(HideFullscreen));
    static bool _rim = ReadSwitch(nameof(Rim)), _appVolume = ReadSwitch(nameof(AppVolume));
    static bool _taskToast = ReadSwitch(nameof(TaskToast)), _taskChime = ReadSwitch(nameof(TaskChime));
    static bool _clipboard = ReadSwitch(nameof(Clipboard));
    static bool _dots = ReadSwitch(nameof(Dots), false);
    static bool _glass = ReadSwitch(nameof(Glass), false);
    static Backdrop _backdrop = (Backdrop)Math.Clamp(Read(nameof(Backdrop), 0), 0, (int)Backdrop.MatrixAndStars);
    static Hover _hover = (Hover)Math.Clamp(Read(nameof(Hover), 0), 0, (int)Hover.Flow);
    static SeekHover _seekHover = (SeekHover)Math.Clamp(Read(nameof(SeekHover), 0), 0, (int)SeekHover.Wave);
    static int _scale = Math.Clamp(Read(nameof(Scale), DefaultScale), MinScale, MaxScale);
    static int _gap = Math.Clamp(Read(nameof(Gap), DefaultGap), 0, MaxGap);
    static int _accent = Read(nameof(Accent), 0);

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

    public static int Scale
    {
        get => _scale;
        set => Write(nameof(Scale), _scale = Math.Clamp(value, MinScale, MaxScale));
    }

    public static int Gap
    {
        get => _gap;
        set => Write(nameof(Gap), _gap = Math.Clamp(value, 0, MaxGap));
    }

    public static Color? Accent
    {
        get => _accent == 0 ? null : Color.FromRgb((byte)(_accent >> 16), (byte)(_accent >> 8), (byte)_accent);
        set => Write(nameof(Accent), _accent = value is { } c ? c.R << 16 | c.G << 8 | c.B : 0);
    }

    static bool ReadSwitch(string name, bool fallback = true) => Read(name, fallback ? 1 : 0) != 0;

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
