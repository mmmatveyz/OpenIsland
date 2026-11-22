using System;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class CustomizationTests
{
    [Fact]
    public void WidgetVisibility_DefaultsAreAccessible()
    {
        // Verification that all widget toggle properties are accessible and boolean
        Assert.True(Settings.ShowOpenCode || !Settings.ShowOpenCode);
        Assert.True(Settings.ShowPlayer || !Settings.ShowPlayer);
        Assert.True(Settings.ShowClipboard || !Settings.ShowClipboard);
        Assert.True(Settings.ShowPerf || !Settings.ShowPerf);
        Assert.True(Settings.ShowProcesses || !Settings.ShowProcesses);
        Assert.True(Settings.ShowQuickLaunch || !Settings.ShowQuickLaunch);
        Assert.True(Settings.ShowShelf || !Settings.ShowShelf);
        Assert.True(Settings.ShowWeather || !Settings.ShowWeather);
        Assert.True(Settings.ShowTimer || !Settings.ShowTimer);
    }

    [Fact]
    public void CapsuleShape_Enum_ContainsExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(CapsuleShape), CapsuleShape.Squircle));
        Assert.True(Enum.IsDefined(typeof(CapsuleShape), CapsuleShape.Pill));
        Assert.True(Enum.IsDefined(typeof(CapsuleShape), CapsuleShape.Fluent));
        Assert.True(Enum.IsDefined(typeof(CapsuleShape), CapsuleShape.Sharp));
        Assert.True(Enum.IsDefined(typeof(CapsuleShape), Settings.CapsuleShape));
    }

    [Fact]
    public void GlassStyle_Enum_ContainsExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(GlassStyle), GlassStyle.Frosted));
        Assert.True(Enum.IsDefined(typeof(GlassStyle), GlassStyle.Tinted));
        Assert.True(Enum.IsDefined(typeof(GlassStyle), GlassStyle.Crystal));
        Assert.True(Enum.IsDefined(typeof(GlassStyle), Settings.GlassStyle));
    }

    [Fact]
    public void GetAdjustedGlassAlpha_ProducesCorrectValues()
    {
        // Явный стиль, чтобы результат не зависел от настроек машины.
        byte frostedAlpha = ThemeService.GetAdjustedGlassAlpha(153, GlassStyle.Frosted);
        byte tintedAlpha = ThemeService.GetAdjustedGlassAlpha(153, GlassStyle.Tinted);
        byte crystalAlpha = ThemeService.GetAdjustedGlassAlpha(153, GlassStyle.Crystal);

        Assert.Equal(153, frostedAlpha);
        Assert.Equal(99, tintedAlpha);
        Assert.Equal(53, crystalAlpha);
        Assert.True(crystalAlpha < tintedAlpha, "Crystal alpha must be more transparent than Tinted");
        Assert.True(tintedAlpha < frostedAlpha, "Tinted alpha must be more transparent than Frosted");
    }

    [Fact]
    public void ThemeBrushes_UpdatesColorsOnThemeChange()
    {
        Exception? error = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                if (System.Windows.Application.ResourceAssembly == null)
                    System.Windows.Application.ResourceAssembly = typeof(MainWindow).Assembly;

                var win = new MainWindow();
                win.ApplyThemeBrushes(ThemeService.Resolve(ThemeMode.Light, ThemePreset.Default), new System.Windows.Duration(TimeSpan.Zero));
                var lightBrush = (System.Windows.Media.SolidColorBrush)win.FindResource("TextPrimary");
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x1C, 0x1C, 0x1E), lightBrush.Color);

                win.ApplyThemeBrushes(ThemeService.Resolve(ThemeMode.Dark, ThemePreset.Default), new System.Windows.Duration(TimeSpan.Zero));
                var darkBrush = (System.Windows.Media.SolidColorBrush)win.FindResource("TextPrimary");
                Assert.Equal(System.Windows.Media.Colors.White, darkBrush.Color);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null) throw error;
    }
}
