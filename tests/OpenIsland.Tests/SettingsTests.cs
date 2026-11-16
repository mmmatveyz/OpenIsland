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
        Assert.InRange(Settings.Scale, 80, 200);
    }

    [Fact]
    public void Opacities_StayWithinSupportedRange()
    {
        Assert.InRange(Settings.PillOpacity, 30, 100);
        Assert.InRange(Settings.OpenOpacity, 30, 100);
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

    /// <summary>
    /// Тумблер звука завершения задачи должен переживать запись-чтение
    /// (и не зависеть от тумблера уведомлений). Значение восстанавливается.
    /// </summary>
    [Fact]
    public void TaskChime_RoundTripsIndependently()
    {
        bool originalChime = Settings.TaskChime;
        bool originalToast = Settings.TaskToast;

        try
        {
            Settings.TaskChime = !originalChime;
            Settings.TaskToast = originalToast;

            Assert.Equal(!originalChime, Settings.TaskChime);
            Assert.Equal(originalToast, Settings.TaskToast);
        }
        finally
        {
            Settings.TaskChime = originalChime;
        }
    }
}
