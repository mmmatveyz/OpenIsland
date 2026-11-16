using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Чистая логика списка сессий: порядок, поиск, имя папки и относительное время.
/// </summary>
public class SessionListTests
{
    static OpenCodeSessionInfo Session(string id, string title, string directory, long updated, bool running = false)
        => new(id, title, directory, "model", "provider", running, updated);

    [Fact]
    public void Order_PutsNewestFirst()
    {
        OpenCodeSessionInfo[] sessions =
        [
            Session("a", "A", @"C:\x", 100),
            Session("b", "B", @"C:\y", 300),
            Session("c", "C", @"C:\z", 200),
        ];

        List<OpenCodeSessionInfo> ordered = SessionList.Order(sessions);

        Assert.Equal(new[] { "b", "c", "a" }, ordered.Select(session => session.Id));
    }

    [Fact]
    public void Filter_EmptyQueryKeepsEverySessionOrdered()
    {
        OpenCodeSessionInfo[] sessions =
        [
            Session("a", "A", @"C:\x", 100),
            Session("b", "B", @"C:\y", 300),
        ];

        Assert.Equal(2, SessionList.Filter(sessions, "").Count);
        Assert.Equal(2, SessionList.Filter(sessions, "   ").Count);
        Assert.Equal(2, SessionList.Filter(sessions, null).Count);
        Assert.Equal(new[] { "b", "a" }, SessionList.Filter(sessions, "").Select(session => session.Id));
    }

    [Fact]
    public void Filter_MatchesTitleOrDirectoryIgnoringCase()
    {
        OpenCodeSessionInfo[] sessions =
        [
            Session("a", "Правка README", @"C:\work\alpha", 100),
            Session("b", "Сборка релиза", @"C:\work\Beta", 200),
        ];

        Assert.Equal(new[] { "a" }, SessionList.Filter(sessions, "readme").Select(session => session.Id));
        Assert.Equal(new[] { "b" }, SessionList.Filter(sessions, "beta").Select(session => session.Id));
        Assert.Equal(new[] { "b", "a" }, SessionList.Filter(sessions, @"C:\WORK").Select(session => session.Id));
        Assert.Empty(SessionList.Filter(sessions, "missing"));
    }

    [Theory]
    [InlineData(@"C:\Users\me\IdeaProjects\OpenIsland", "OpenIsland")]
    [InlineData(@"C:\Users\me\OpenIsland\", "OpenIsland")]
    [InlineData("C:/Users/me/proj", "proj")]
    [InlineData(@"C:\", "C:")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void FolderName_ReturnsLastSegment(string? directory, string expected)
    {
        Assert.Equal(expected, SessionList.FolderName(directory));
    }

    [Fact]
    public void RelativeTime_FormatsRecentAges()
    {
        DateTime now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        long At(TimeSpan ago) => new DateTimeOffset(now - ago).ToUnixTimeMilliseconds();

        Assert.Equal("сейчас", SessionList.RelativeTime(At(TimeSpan.FromSeconds(30)), now));
        Assert.Equal("5 мин", SessionList.RelativeTime(At(TimeSpan.FromMinutes(5)), now));
        Assert.Equal("2 ч", SessionList.RelativeTime(At(TimeSpan.FromHours(2)), now));
        Assert.Equal("3 д", SessionList.RelativeTime(At(TimeSpan.FromDays(3)), now));
    }

    [Fact]
    public void RelativeTime_UsesDateOnceOlderThanAWeek()
    {
        DateTime now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
        long old = new DateTimeOffset(now - TimeSpan.FromDays(30)).ToUnixTimeMilliseconds();

        Assert.Matches(@"^\d{2}\.\d{2}$", SessionList.RelativeTime(old, now));
    }

    [Fact]
    public void RelativeTime_HandlesMissingClockSkewAndFuture()
    {
        DateTime now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal("", SessionList.RelativeTime(0, now));
        Assert.Equal("", SessionList.RelativeTime(-5, now));

        long future = new DateTimeOffset(now + TimeSpan.FromMinutes(3)).ToUnixTimeMilliseconds();
        Assert.Equal("сейчас", SessionList.RelativeTime(future, now));
    }

    [Fact]
    public void SearchThreshold_IsHighEnoughToAvoidNoise()
    {
        Assert.Equal(8, SessionList.SearchThreshold);
    }
}
