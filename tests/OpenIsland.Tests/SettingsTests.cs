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
    public void NoticeSeconds_StaysWithinSupportedRange()
    {
        Assert.InRange(Settings.NoticeSeconds, 2, 10);
    }

    [Fact]
    public void NoticeSeconds_CyclesThroughPresets()
    {
        Assert.Equal(new[] { 2, 3, 4, 6, 8, 10 }, Settings.NoticePresets);
        Assert.Equal(3, Settings.NextNoticeSeconds(2));
        Assert.Equal(4, Settings.NextNoticeSeconds(3));
        Assert.Equal(6, Settings.NextNoticeSeconds(4));
        Assert.Equal(8, Settings.NextNoticeSeconds(6));
        Assert.Equal(10, Settings.NextNoticeSeconds(8));
        Assert.Equal(2, Settings.NextNoticeSeconds(10));
        // Значение вне пресетов (например, из реестра) уходит к ближайшему большему.
        Assert.Equal(6, Settings.NextNoticeSeconds(5));
        Assert.Equal(2, Settings.NextNoticeSeconds(11));
    }

    [Fact]
    public void NoticeSeconds_ClampsAndRestores()
    {
        int original = Settings.NoticeSeconds;
        try
        {
            Settings.NoticeSeconds = 1;
            Assert.Equal(2, Settings.NoticeSeconds);

            Settings.NoticeSeconds = 99;
            Assert.Equal(10, Settings.NoticeSeconds);
        }
        finally
        {
            Settings.NoticeSeconds = original;
        }
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

    [Fact]
    public void HideFromCapture_RoundTrips()
    {
        bool original = Settings.HideFromCapture;
        try
        {
            Settings.HideFromCapture = !original;
            Assert.Equal(!original, Settings.HideFromCapture);
        }
        finally
        {
            Settings.HideFromCapture = original;
        }
    }

    [Fact]
    public void FullscreenExceptions_RoundTrips_And_IsFullscreenExcluded_Matches()
    {
        string original = Settings.FullscreenExceptions;
        try
        {
            Settings.FullscreenExceptions = "code, notepad.exe, vlc";
            Assert.Equal("code, notepad.exe, vlc", Settings.FullscreenExceptions);

            Assert.True(Settings.IsFullscreenExcluded("code"));
            Assert.True(Settings.IsFullscreenExcluded("code.exe"));
            Assert.True(Settings.IsFullscreenExcluded("notepad"));
            Assert.True(Settings.IsFullscreenExcluded("notepad.exe"));
            Assert.True(Settings.IsFullscreenExcluded("VLC"));
            Assert.True(Settings.IsFullscreenExcluded("VLC.EXE"));

            Assert.False(Settings.IsFullscreenExcluded("chrome"));
            Assert.False(Settings.IsFullscreenExcluded(""));
            Assert.False(Settings.IsFullscreenExcluded(null));
        }
        finally
        {
            Settings.FullscreenExceptions = original;
        }
    }
}
