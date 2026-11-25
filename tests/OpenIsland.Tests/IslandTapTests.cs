using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Приоритет карточек по клику на свёрнутый островок: плеер важнее агента,
/// агент важнее таймера, а выключенный виджет OpenCode не открывается вовсе.
/// </summary>
public class IslandTapTests
{
    [Fact]
    public void Media_WinsOverEverything()
    {
        TapTarget target = IslandTap.Target(showOpenCode: true, mediaActive: true, agentBusy: true, timerActive: true, agentConnected: true);

        Assert.Equal(TapTarget.Player, target);
    }

    [Fact]
    public void BusyAgent_OpensOpenCode_WhenWidgetEnabled()
    {
        TapTarget target = IslandTap.Target(showOpenCode: true, mediaActive: false, agentBusy: true, timerActive: false, agentConnected: true);

        Assert.Equal(TapTarget.OpenCode, target);
    }

    [Fact]
    public void ConnectedAgent_OpensOpenCode_WhenWidgetEnabled()
    {
        TapTarget target = IslandTap.Target(showOpenCode: true, mediaActive: false, agentBusy: false, timerActive: false, agentConnected: true);

        Assert.Equal(TapTarget.OpenCode, target);
    }

    [Fact]
    public void BusyAgent_BeatsTimer_WhenWidgetEnabled()
    {
        TapTarget target = IslandTap.Target(showOpenCode: true, mediaActive: false, agentBusy: true, timerActive: true, agentConnected: true);

        Assert.Equal(TapTarget.OpenCode, target);
    }

    [Fact]
    public void Timer_Opens_WhenAgentIdle()
    {
        TapTarget target = IslandTap.Target(showOpenCode: true, mediaActive: false, agentBusy: false, timerActive: true, agentConnected: true);

        Assert.Equal(TapTarget.Timer, target);
    }

    [Fact]
    public void DisabledWidget_DoesNotOpenOpenCode_EvenWhenAgentBusy()
    {
        TapTarget target = IslandTap.Target(showOpenCode: false, mediaActive: false, agentBusy: true, timerActive: false, agentConnected: true);

        Assert.Equal(TapTarget.Menu, target);
    }

    [Fact]
    public void DisabledWidget_DoesNotOpenOpenCode_EvenWhenConnected()
    {
        TapTarget target = IslandTap.Target(showOpenCode: false, mediaActive: false, agentBusy: false, timerActive: false, agentConnected: true);

        Assert.Equal(TapTarget.Menu, target);
    }

    [Fact]
    public void DisabledWidget_StillOpensTimer()
    {
        TapTarget target = IslandTap.Target(showOpenCode: false, mediaActive: false, agentBusy: false, timerActive: true, agentConnected: true);

        Assert.Equal(TapTarget.Timer, target);
    }

    [Fact]
    public void NothingActive_OpensMenu()
    {
        TapTarget target = IslandTap.Target(showOpenCode: true, mediaActive: false, agentBusy: false, timerActive: false, agentConnected: false);

        Assert.Equal(TapTarget.Menu, target);
    }
}
