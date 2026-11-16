using System.Windows.Media;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Инварианты настроек. Тест ничего не пишет в реестр: он проверяет, что
/// значения, прочитанные из `HKCU\Software\OpenIsland` (или их дефолты),
/// всегда приходят в допустимом диапазоне. Пороги совпадают с `Settings`.
/// </summary>
public class SettingsTests
{
    [Fact]
    public void Scale_StaysWithinSupportedRange()
    {
        Assert.InRange(Settings.Scale, 85, 130);
    }

    [Fact]
    public void Gap_StaysWithinSupportedRange()
    {
        Assert.InRange(Settings.Gap, 0, 24);
    }

    [Fact]
    public void EnumeratedChoices_StayWithinTheirDeclaredRange()
    {
        Assert.InRange((int)Settings.Backdrop, (int)Backdrop.Glow, (int)Backdrop.MatrixAndStars);
        Assert.InRange((int)Settings.Hover, (int)Hover.Disc, (int)Hover.Flow);
        Assert.InRange((int)Settings.SeekHover, (int)SeekHover.Magnifier, (int)SeekHover.Wave);
    }

    [Fact]
    public void Accent_IsEitherUnsetOrAnOpaqueColor()
    {
        Color? accent = Settings.Accent;

        if (accent is { } color) Assert.Equal((byte)0xFF, color.A);
    }
}
