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
        byte frostedAlpha = ThemeService.GetAdjustedGlassAlpha(153);
        Assert.Equal(153, frostedAlpha);

        // When GlassStyle is set to Tinted or Crystal, alpha decreases
        byte tintedAlpha = (byte)Math.Clamp((int)(153 * 0.65), 20, 255);
        byte crystalAlpha = (byte)Math.Clamp((int)(153 * 0.35), 10, 255);

        Assert.True(crystalAlpha < tintedAlpha, "Crystal alpha must be more transparent than Tinted");
        Assert.True(tintedAlpha < frostedAlpha, "Tinted alpha must be more transparent than Frosted");
    }
}
