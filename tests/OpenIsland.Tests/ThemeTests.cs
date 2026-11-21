using System;
using System.Windows.Media;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData(ThemeMode.Dark, ThemePreset.Default)]
    [InlineData(ThemeMode.Dark, ThemePreset.Oled)]
    [InlineData(ThemeMode.Dark, ThemePreset.Nord)]
    [InlineData(ThemeMode.Dark, ThemePreset.Cyberpunk)]
    [InlineData(ThemeMode.Light, ThemePreset.Default)]
    [InlineData(ThemeMode.Light, ThemePreset.Oled)]
    [InlineData(ThemeMode.Light, ThemePreset.Nord)]
    [InlineData(ThemeMode.Light, ThemePreset.Cyberpunk)]
    [InlineData(ThemeMode.System, ThemePreset.Default)]
    public void Resolve_AllModesAndPresets_ProducesValidPalette(ThemeMode mode, ThemePreset preset)
    {
        ThemePalette p = ThemeService.Resolve(mode, preset);

        Assert.NotNull(p);
        Assert.True(p.TextPrimary.A > 0, "TextPrimary must be opaque or visible");
        Assert.True(p.CardBackground.A > 0, "CardBackground must have non-zero alpha");
        Assert.True(p.IslandFill.A > 0, "IslandFill must have non-zero alpha");
        Assert.True(p.GlassAlpha > 0, "GlassAlpha must be > 0");
    }

    [Fact]
    public void Resolve_DarkMode_IsDarkIsTrue()
    {
        ThemePalette p = ThemeService.Resolve(ThemeMode.Dark, ThemePreset.Default);
        Assert.True(p.IsDark);
        Assert.Equal(Colors.White, p.TextPrimary);
    }

    [Fact]
    public void Resolve_LightMode_IsDarkIsFalse()
    {
        ThemePalette p = ThemeService.Resolve(ThemeMode.Light, ThemePreset.Default);
        Assert.False(p.IsDark);
        Assert.Equal(Color.FromRgb(0x1C, 0x1C, 0x1E), p.TextPrimary);
    }

    [Fact]
    public void Resolve_OledDark_HasPureBlackBackground()
    {
        ThemePalette p = ThemeService.Resolve(ThemeMode.Dark, ThemePreset.Oled);
        Assert.Equal(Colors.Black, p.IslandFill);
        Assert.Equal(Colors.Black, p.CardBackground);
    }

    [Fact]
    public void Resolve_NordDark_HasPolarNightBackground()
    {
        ThemePalette p = ThemeService.Resolve(ThemeMode.Dark, ThemePreset.Nord);
        Assert.Equal(Color.FromRgb(0x24, 0x29, 0x33), p.IslandFill);
        Assert.Equal(Color.FromRgb(0xEC, 0xEF, 0xF4), p.TextPrimary);
    }

    [Fact]
    public void Resolve_CyberpunkDark_HasNeonPalette()
    {
        ThemePalette p = ThemeService.Resolve(ThemeMode.Dark, ThemePreset.Cyberpunk);
        Assert.Equal(Color.FromRgb(0x12, 0x0A, 0x21), p.IslandFill);
        Assert.Equal(Color.FromArgb(0x40, 0xFF, 0x71, 0xCE), p.IslandRim);
    }

    [Fact]
    public void Shades_Auto_SwitchesBetweenWhiteAndBlack()
    {
        var darkBrush = Assert.IsType<SolidColorBrush>(Shades.Auto(isDark: true, opacity: 0.5));
        var lightBrush = Assert.IsType<SolidColorBrush>(Shades.Auto(isDark: false, opacity: 0.5));

        Assert.Equal(Colors.White.R, darkBrush.Color.R);
        Assert.Equal(Colors.White.G, darkBrush.Color.G);
        Assert.Equal(Colors.White.B, darkBrush.Color.B);
        Assert.InRange(darkBrush.Color.A, 126, 129);

        Assert.Equal(Colors.Black.R, lightBrush.Color.R);
        Assert.Equal(Colors.Black.G, lightBrush.Color.G);
        Assert.Equal(Colors.Black.B, lightBrush.Color.B);
        Assert.InRange(lightBrush.Color.A, 126, 129);
    }

    [Fact]
    public void Settings_ThemeEnums_AreValid()
    {
        Assert.True(Enum.IsDefined(typeof(ThemeMode), Settings.ThemeMode));
        Assert.True(Enum.IsDefined(typeof(ThemePreset), Settings.ThemePreset));
    }
}
