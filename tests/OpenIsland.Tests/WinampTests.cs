using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Разбор заголовков Winamp-совместимых плееров (Winamp, WACUP, AIMP) и
/// преобразование скалярных ответов IPC в состояние, позицию и длительность.
/// </summary>
public class WinampTests
{
    [Theory]
    [InlineData("2. Metallica - Master of Puppets - Winamp", "Metallica", "Master of Puppets")]
    [InlineData("Metallica - Master of Puppets - AIMP", "Metallica", "Master of Puppets")]
    [InlineData("Daft Punk - One More Time - WACUP v1.0.20", "Daft Punk", "One More Time")]
    [InlineData("Nirvana - Lithium - Winamp 5.666", "Nirvana", "Lithium")]
    public void Parse_StripsPlaylistNumberAndPlayerSuffix(string raw, string artist, string title)
    {
        Assert.Equal((artist, title), WinampTitle.Parse(raw));
    }

    [Fact]
    public void Parse_RemovesPausedMarker()
    {
        Assert.Equal(("Metallica", "Master of Puppets"),
            WinampTitle.Parse("2. Metallica - Master of Puppets - Winamp [Paused]"));
    }

    [Fact]
    public void Parse_KeepsDashesInsideTitle()
    {
        Assert.Equal(("AC/DC", "Live - Thunderstruck"),
            WinampTitle.Parse("AC/DC - Live - Thunderstruck - Winamp"));
    }

    [Theory]
    [InlineData("Just a Title", "", "Just a Title")]
    [InlineData("Song - AIMP", "", "Song")]
    [InlineData("12. Song - Apollo", "", "Song")]
    public void Parse_HandlesMissingArtist(string raw, string artist, string title)
    {
        Assert.Equal((artist, title), WinampTitle.Parse(raw));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Winamp")]
    [InlineData("AIMP v4.60")]
    [InlineData("Winamp [Stopped]")]
    public void Parse_IgnoresPlayerOnlyTitles(string raw)
    {
        Assert.Equal(("", ""), WinampTitle.Parse(raw));
    }

    [Fact]
    public void Parse_DoesNotStripDotWithoutSpace()
    {
        Assert.Equal(("", "24.5 Hours"), WinampTitle.Parse("24.5 Hours"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(7, 0)]
    [InlineData(-1, 0)]
    public void StateOf_MapsScalarIpcResult(long code, int expected)
    {
        Assert.Equal(expected, (int)WinampIpc.StateOf(code));
    }

    [Fact]
    public void PositionFromMilliseconds_ConvertsAndRejectsUnavailable()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(61500), WinampIpc.PositionFromMilliseconds(61500));
        Assert.Equal(TimeSpan.Zero, WinampIpc.PositionFromMilliseconds(-1));
    }

    [Fact]
    public void DurationFromSeconds_ConvertsAndRejectsUnavailable()
    {
        Assert.Equal(TimeSpan.FromMinutes(3), WinampIpc.DurationFromSeconds(180));
        Assert.Equal(TimeSpan.Zero, WinampIpc.DurationFromSeconds(-1));
    }

    [Fact]
    public void CommandAndSelectorValues_MatchWinampApi()
    {
        Assert.Equal(104, WinampIpc.IsPlayingSelector);
        Assert.Equal(105, WinampIpc.OutputTimeSelector);
        Assert.Equal(106, WinampIpc.JumpToTimeSelector);
        Assert.Equal(40044, WinampIpc.PreviousCommand);
        Assert.Equal(40045, WinampIpc.PlayCommand);
        Assert.Equal(40046, WinampIpc.PauseCommand);
        Assert.Equal(40048, WinampIpc.NextCommand);
    }
}
