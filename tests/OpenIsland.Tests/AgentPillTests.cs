using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Приоритет состояний индикатора агента в свёрнутой пилюле:
/// разрешение важнее работы, «готово» живёт ограниченное время.
/// </summary>
public class AgentPillTests
{
    static readonly TimeSpan Never = TimeSpan.MaxValue;

    [Fact]
    public void Disconnected_IsHiddenEvenWhenBusy()
    {
        AgentPillState state = AgentPill.StateOf(connected: false, busy: true, asks: true, sinceDone: TimeSpan.Zero);

        Assert.Equal(AgentPillState.Hidden, state);
    }

    [Fact]
    public void Permission_WinsOverBusyAndDone()
    {
        AgentPillState state = AgentPill.StateOf(connected: true, busy: true, asks: true, sinceDone: TimeSpan.Zero);

        Assert.Equal(AgentPillState.Asks, state);
    }

    [Fact]
    public void Busy_WinsOverRecentDone()
    {
        AgentPillState state = AgentPill.StateOf(connected: true, busy: true, asks: false, sinceDone: TimeSpan.Zero);

        Assert.Equal(AgentPillState.Busy, state);
    }

    [Fact]
    public void RecentDone_IsShown()
    {
        AgentPillState state = AgentPill.StateOf(connected: true, busy: false, asks: false, sinceDone: TimeSpan.FromSeconds(1));

        Assert.Equal(AgentPillState.Done, state);
    }

    [Fact]
    public void Done_ExpiresAfterLinger()
    {
        AgentPillState state = AgentPill.StateOf(
            connected: true, busy: false, asks: false, sinceDone: AgentPill.DoneLinger);

        Assert.Equal(AgentPillState.Hidden, state);
    }

    [Fact]
    public void MissingOrFutureDoneStamp_IsHidden()
    {
        Assert.Equal(AgentPillState.Hidden, AgentPill.StateOf(true, false, false, Never));
        Assert.Equal(AgentPillState.Hidden, AgentPill.StateOf(true, false, false, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void OnlineIdleWithoutDone_IsHidden()
    {
        AgentPillState state = AgentPill.StateOf(connected: true, busy: false, asks: false, sinceDone: Never);

        Assert.Equal(AgentPillState.Hidden, state);
    }
}