using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

public class CountdownTests
{
    [Fact]
    public void Start_ActivatesAndRunsFromFullTotal()
    {
        var countdown = new Countdown();

        countdown.Start(TimeSpan.FromMinutes(5));

        Assert.True(countdown.IsActive);
        Assert.True(countdown.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(5), countdown.Total);
        Assert.InRange(countdown.Remaining, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        Assert.InRange(countdown.RemainingShare, 0.99, 1.0);
    }

    [Fact]
    public void Toggle_PausesAndResumes()
    {
        var countdown = new Countdown();
        countdown.Start(TimeSpan.FromMinutes(10));

        countdown.Toggle();
        Assert.False(countdown.IsRunning);
        Assert.True(countdown.IsActive);
        TimeSpan paused = countdown.Remaining;

        Thread.Sleep(20);
        Assert.Equal(paused, countdown.Remaining);

        countdown.Toggle();
        Assert.True(countdown.IsRunning);
        Assert.True(countdown.Remaining <= paused);
    }

    [Fact]
    public void Stop_ResetsToIdle()
    {
        var countdown = new Countdown();
        countdown.Start(TimeSpan.FromMinutes(5));

        countdown.Stop();

        Assert.False(countdown.IsActive);
        Assert.False(countdown.IsRunning);
        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
        Assert.Equal(0d, countdown.RemainingShare);
    }

    [Fact]
    public void Toggle_BeforeStartDoesNothing()
    {
        var countdown = new Countdown();

        countdown.Toggle();

        Assert.False(countdown.IsActive);
        Assert.False(countdown.IsRunning);
        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
    }

    [Fact]
    public void ZeroTotal_HasNoShareAndNeverGoesNegative()
    {
        var countdown = new Countdown();

        countdown.Start(TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, countdown.Remaining);
        Assert.Equal(0d, countdown.RemainingShare);
    }
}
