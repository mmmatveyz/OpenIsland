using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace OpenIsland;

public sealed record ThemePalette(
    bool IsDark,
    Color IslandFill,
    Color IslandRim,
    Color TextPrimary,
    Color TextDim,
    Color TextFaint,
    Color CardBackground,
    Color CardSubBackground,
    Color CardSectionBackground,
    Color CardBorder,
    Color ToggleOff,
    Color ChipBackground,
    Color HoverBackground,
    Color PressedBackground,
    Color ShadowColor,
    double ShadowOpacity,
    byte GlassAlpha
);

public static class ThemeService
{
    static readonly SolidColorBrush StaticPrimaryBrush = new(Colors.White);

    public static ThemePalette CurrentPalette { get; private set; } = Resolve(ThemeMode.Dark, ThemePreset.Default);

    public static bool IsDark => CurrentPalette.IsDark;

    public static Brush PrimaryBrush => StaticPrimaryBrush;

    public static SolidColorBrush PrimarySolidBrush => StaticPrimaryBrush;

    public static ThemeMode EffectiveThemeMode =>
        Settings.ThemeMode == ThemeMode.System
            ? (IsWindowsLightMode() ? ThemeMode.Light : ThemeMode.Dark)
            : Settings.ThemeMode;

    public static bool IsWindowsLightMode()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? val = key?.GetValue("AppsUseLightTheme");
            if (val is int intVal) return intVal != 0;
        }
        catch
        {
            // Fallback to dark if registry cannot be read
        }
        return false;
    }

    public static byte GetAdjustedGlassAlpha(byte baseAlpha) =>
        GetAdjustedGlassAlpha(baseAlpha, Settings.GlassStyle);

    public static byte GetAdjustedGlassAlpha(byte baseAlpha, GlassStyle style) => style switch
    {
        GlassStyle.Frosted => baseAlpha,
        GlassStyle.Tinted => (byte)Math.Clamp((int)(baseAlpha * 0.65), 20, 255),
        GlassStyle.Crystal => (byte)Math.Clamp((int)(baseAlpha * 0.35), 10, 255),
        _ => baseAlpha
    };

    public static ThemePalette Resolve(ThemeMode mode, ThemePreset preset)
    {
        bool light = mode == ThemeMode.Light || (mode == ThemeMode.System && IsWindowsLightMode());
        ThemePalette p = light ? ResolveLight(preset) : ResolveDark(preset);
        byte adjustedAlpha = GetAdjustedGlassAlpha(p.GlassAlpha);
        return p with { GlassAlpha = adjustedAlpha };
    }

    static ThemePalette ResolveDark(ThemePreset preset) => preset switch
    {
        ThemePreset.Oled => new ThemePalette(
            IsDark: true,
            IslandFill: Colors.Black,
            IslandRim: Color.FromArgb(0x30, 255, 255, 255),
            TextPrimary: Colors.White,
            TextDim: Color.FromArgb(0x99, 255, 255, 255),
            TextFaint: Color.FromArgb(0x66, 255, 255, 255),
            CardBackground: Color.FromRgb(0x00, 0x00, 0x00),
            CardSubBackground: Color.FromRgb(0x12, 0x12, 0x12),
            CardSectionBackground: Color.FromArgb(0x18, 255, 255, 255),
            CardBorder: Color.FromRgb(0x28, 0x28, 0x28),
            ToggleOff: Color.FromRgb(0x2A, 0x2A, 0x2D),
            ChipBackground: Color.FromArgb(0x18, 255, 255, 255),
            HoverBackground: Color.FromArgb(0x20, 255, 255, 255),
            PressedBackground: Color.FromArgb(0x30, 255, 255, 255),
            ShadowColor: Colors.Black,
            ShadowOpacity: 0.5,
            GlassAlpha: 0xFF
        ),

        ThemePreset.Nord => new ThemePalette(
            IsDark: true,
            IslandFill: Color.FromRgb(0x24, 0x29, 0x33),
            IslandRim: Color.FromArgb(0x30, 0x88, 0xC0, 0xD0),
            TextPrimary: Color.FromRgb(0xEC, 0xEF, 0xF4),
            TextDim: Color.FromArgb(0x99, 0xD8, 0xDE, 0xE9),
            TextFaint: Color.FromArgb(0x66, 0xD8, 0xDE, 0xE9),
            CardBackground: Color.FromArgb(0xE6, 0x2E, 0x34, 0x40),
            CardSubBackground: Color.FromRgb(0x3B, 0x42, 0x52),
            CardSectionBackground: Color.FromArgb(0x20, 0x88, 0xC0, 0xD0),
            CardBorder: Color.FromRgb(0x4C, 0x56, 0x6A),
            ToggleOff: Color.FromRgb(0x43, 0x4C, 0x5E),
            ChipBackground: Color.FromArgb(0x20, 0x88, 0xC0, 0xD0),
            HoverBackground: Color.FromArgb(0x18, 0x88, 0xC0, 0xD0),
            PressedBackground: Color.FromArgb(0x30, 0x88, 0xC0, 0xD0),
            ShadowColor: Color.FromRgb(0x1B, 0x1F, 0x27),
            ShadowOpacity: 0.4,
            GlassAlpha: 0x99
        ),

        ThemePreset.Cyberpunk => new ThemePalette(
            IsDark: true,
            IslandFill: Color.FromRgb(0x12, 0x0A, 0x21),
            IslandRim: Color.FromArgb(0x40, 0xFF, 0x71, 0xCE),
            TextPrimary: Color.FromRgb(0xFD, 0xFB, 0xFF),
            TextDim: Color.FromArgb(0xAA, 0x01, 0xCD, 0xFE),
            TextFaint: Color.FromArgb(0x77, 0xFF, 0x71, 0xCE),
            CardBackground: Color.FromArgb(0xE6, 0x18, 0x0F, 0x2C),
            CardSubBackground: Color.FromRgb(0x25, 0x18, 0x42),
            CardSectionBackground: Color.FromArgb(0x20, 0x01, 0xCD, 0xFE),
            CardBorder: Color.FromRgb(0x58, 0x2B, 0x8C),
            ToggleOff: Color.FromRgb(0x36, 0x21, 0x5D),
            ChipBackground: Color.FromArgb(0x25, 0xFF, 0x71, 0xCE),
            HoverBackground: Color.FromArgb(0x20, 0x01, 0xCD, 0xFE),
            PressedBackground: Color.FromArgb(0x35, 0xFF, 0x71, 0xCE),
            ShadowColor: Color.FromRgb(0x0C, 0x05, 0x17),
            ShadowOpacity: 0.45,
            GlassAlpha: 0x99
        ),

        _ => new ThemePalette( // Default Obsidian
            IsDark: true,
            IslandFill: Colors.Black,
            IslandRim: Color.FromArgb(0x20, 255, 255, 255),
            TextPrimary: Colors.White,
            TextDim: Color.FromArgb(0x99, 255, 255, 255),
            TextFaint: Color.FromArgb(0x66, 255, 255, 255),
            CardBackground: Color.FromArgb(0xE6, 0x1C, 0x1C, 0x1E),
            CardSubBackground: Color.FromRgb(0x2C, 0x2C, 0x2E),
            CardSectionBackground: Color.FromArgb(0x1A, 255, 255, 255),
            CardBorder: Color.FromRgb(0x3A, 0x3A, 0x3C),
            ToggleOff: Color.FromRgb(0x39, 0x39, 0x3D),
            ChipBackground: Color.FromArgb(0x1F, 255, 255, 255),
            HoverBackground: Color.FromArgb(0x14, 255, 255, 255),
            PressedBackground: Color.FromArgb(0x24, 255, 255, 255),
            ShadowColor: Colors.Black,
            ShadowOpacity: 0.35,
            GlassAlpha: 0x99
        )
    };

    static ThemePalette ResolveLight(ThemePreset preset) => preset switch
    {
        ThemePreset.Oled => new ThemePalette( // Clean Paper
            IsDark: false,
            IslandFill: Color.FromRgb(0xFE, 0xFE, 0xFE),
            IslandRim: Color.FromArgb(0x35, 0, 0, 0),
            TextPrimary: Color.FromRgb(0x11, 0x11, 0x12),
            TextDim: Color.FromRgb(0x66, 0x66, 0x69),
            TextFaint: Color.FromRgb(0x99, 0x99, 0x9E),
            CardBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            CardSubBackground: Color.FromRgb(0xF2, 0xF2, 0xF7),
            CardSectionBackground: Color.FromArgb(0x12, 0, 0, 0),
            CardBorder: Color.FromRgb(0xCC, 0xCC, 0xD2),
            ToggleOff: Color.FromRgb(0xE0, 0xE0, 0xE5),
            ChipBackground: Color.FromArgb(0x10, 0, 0, 0),
            HoverBackground: Color.FromArgb(0x18, 0, 0, 0),
            PressedBackground: Color.FromArgb(0x25, 0, 0, 0),
            ShadowColor: Color.FromRgb(0x20, 0x20, 0x25),
            ShadowOpacity: 0.28,
            GlassAlpha: 0xFF
        ),

        ThemePreset.Nord => new ThemePalette( // Snow / Ice
            IsDark: false,
            IslandFill: Color.FromRgb(0xEC, 0xEF, 0xF4),
            IslandRim: Color.FromArgb(0x35, 0x5E, 0x81, 0xAC),
            TextPrimary: Color.FromRgb(0x2E, 0x34, 0x40),
            TextDim: Color.FromRgb(0x4C, 0x56, 0x6A),
            TextFaint: Color.FromRgb(0x70, 0x7E, 0x99),
            CardBackground: Color.FromArgb(0xE6, 0xF2, 0xF4, 0xF8),
            CardSubBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            CardSectionBackground: Color.FromArgb(0x18, 0x5E, 0x81, 0xAC),
            CardBorder: Color.FromRgb(0xD8, 0xDE, 0xE9),
            ToggleOff: Color.FromRgb(0xD8, 0xDE, 0xE9),
            ChipBackground: Color.FromArgb(0x15, 0x5E, 0x81, 0xAC),
            HoverBackground: Color.FromArgb(0x1A, 0x5E, 0x81, 0xAC),
            PressedBackground: Color.FromArgb(0x2A, 0x5E, 0x81, 0xAC),
            ShadowColor: Color.FromRgb(0x4C, 0x56, 0x6A),
            ShadowOpacity: 0.25,
            GlassAlpha: 0x99
        ),

        ThemePreset.Cyberpunk => new ThemePalette( // Pastel Neon
            IsDark: false,
            IslandFill: Color.FromRgb(0xF8, 0xF2, 0xFC),
            IslandRim: Color.FromArgb(0x35, 0xC4, 0x28, 0xA2),
            TextPrimary: Color.FromRgb(0x2A, 0x14, 0x3D),
            TextDim: Color.FromRgb(0x7A, 0x38, 0x9E),
            TextFaint: Color.FromRgb(0x9E, 0x68, 0xBD),
            CardBackground: Color.FromArgb(0xE6, 0xFC, 0xF8, 0xFE),
            CardSubBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            CardSectionBackground: Color.FromArgb(0x15, 0xC4, 0x28, 0xA2),
            CardBorder: Color.FromRgb(0xE2, 0xD2, 0xEE),
            ToggleOff: Color.FromRgb(0xE8, 0xDD, 0xF0),
            ChipBackground: Color.FromArgb(0x15, 0xC4, 0x28, 0xA2),
            HoverBackground: Color.FromArgb(0x18, 0xC4, 0x28, 0xA2),
            PressedBackground: Color.FromArgb(0x25, 0xC4, 0x28, 0xA2),
            ShadowColor: Color.FromRgb(0x58, 0x2B, 0x8C),
            ShadowOpacity: 0.25,
            GlassAlpha: 0x99
        ),

        _ => new ThemePalette( // Default Frosted Milky Glass
            IsDark: false,
            IslandFill: Color.FromRgb(0xF5, 0xF5, 0xF7),
            IslandRim: Color.FromArgb(0x28, 0, 0, 0),
            TextPrimary: Color.FromRgb(0x1C, 0x1C, 0x1E),
            TextDim: Color.FromRgb(0x73, 0x73, 0x77),
            TextFaint: Color.FromRgb(0xA0, 0xA0, 0xA5),
            CardBackground: Color.FromArgb(0xE6, 0xFA, 0xFA, 0xFC),
            CardSubBackground: Color.FromRgb(0xFF, 0xFF, 0xFF),
            CardSectionBackground: Color.FromArgb(0x0F, 0, 0, 0),
            CardBorder: Color.FromRgb(0xD8, 0xD8, 0xDE),
            ToggleOff: Color.FromRgb(0xE5, 0xE5, 0xEA),
            ChipBackground: Color.FromArgb(0x10, 0, 0, 0),
            HoverBackground: Color.FromArgb(0x15, 0, 0, 0),
            PressedBackground: Color.FromArgb(0x22, 0, 0, 0),
            ShadowColor: Color.FromRgb(0x30, 0x30, 0x35),
            ShadowOpacity: 0.25,
            GlassAlpha: 0x99
        )
    };

    public static void Apply(MainWindow window, bool animate = true)
    {
        ThemePalette p = Resolve(Settings.ThemeMode, Settings.ThemePreset);
        CurrentPalette = p;
        StaticPrimaryBrush.Color = p.TextPrimary;

        Duration duration = animate ? new Duration(TimeSpan.FromMilliseconds(240)) : new Duration(TimeSpan.Zero);

        // Animate/set the dynamic resource brushes in window
        window.ApplyThemeBrushes(p, duration);

        // Update Goo capsule fill & rim
        window.Body.ApplyPalette(p.IslandFill, p.IslandRim, p.GlassAlpha, Settings.Glass, duration);

        // Update media player accent & rim if needed
        window.SyncAccent(animate);
        window.SyncRim();

        // Invalidate visual trees to re-render custom controls
        window.InvalidateVisual();
    }

    public static void WatchSystemTheme(MainWindow window)
    {
        SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (Settings.ThemeMode != ThemeMode.System) return;
            window.Dispatcher.InvokeAsync(() => Apply(window, animate: true));
        };
    }
}
