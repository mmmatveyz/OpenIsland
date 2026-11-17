using System.Net;
using System.Net.Sockets;
using OpenIsland;
using Xunit;

namespace OpenIsland.Tests;

/// <summary>
/// Логика карточки «Процессы и порты»: фильтр dev-инструментов, сортировка,
/// подписи памяти и портов. Плюс живая проверка IP Helper API.
/// </summary>
public class DevProcessesTests
{
    [Theory]
    [InlineData("node")]
    [InlineData("node.exe")]
    [InlineData("python3.11")]
    [InlineData("python")]
    [InlineData("dotnet")]
    [InlineData("com.docker.backend")]
    [InlineData("gopls")]
    [InlineData("php8")]
    [InlineData("wslrelay")]
    [InlineData("ollama")]
    public void IsKnown_RecognizesDevTools(string name) => Assert.True(DevProcesses.IsKnown(name));

    [Theory]
    [InlineData("chrome")]
    [InlineData("svchost")]
    [InlineData("notepad")]
    [InlineData("")]
    [InlineData(null)]
    public void IsKnown_RejectsEverythingElse(string? name) => Assert.False(DevProcesses.IsKnown(name));

    [Fact]
    public void Normalize_StripsExeAndSpaces()
    {
        Assert.Equal("node", DevProcesses.Normalize(" node.EXE "));
        Assert.Equal("python", DevProcesses.Normalize("python"));
        Assert.Equal("", DevProcesses.Normalize(null));
    }

    [Fact]
    public void IsInteresting_UnknownProcessNeedsPort()
    {
        Assert.True(DevProcesses.IsInteresting("custom-server", true));
        Assert.False(DevProcesses.IsInteresting("custom-server", false));
        Assert.True(DevProcesses.IsInteresting("node", false));
    }

    [Fact]
    public void IsInteresting_HidesSystemProcessesEvenWithPorts()
    {
        Assert.False(DevProcesses.IsInteresting("svchost", true));
        Assert.False(DevProcesses.IsInteresting("System", true));
        Assert.False(DevProcesses.IsInteresting("explorer", true));
    }

    [Fact]
    public void Sort_PutsPortsFirstThenBiggestProcesses()
    {
        DevProcessInfo[] items =
        [
            new(1, "idle-node", 500L * 1024 * 1024, []),
            new(2, "small-node", 100L * 1024 * 1024, []),
            new(3, "vite", 10L * 1024 * 1024, [3000, 5173]),
            new(4, "api", 20L * 1024 * 1024, [8080]),
        ];

        IReadOnlyList<DevProcessInfo> ordered = DevProcesses.Sort(items);

        Assert.Equal(new[] { 3, 4, 1, 2 }, ordered.Select(item => item.Pid));
    }

    [Fact]
    public void SortPorts_DeduplicatesFiltersAndOrders()
    {
        Assert.Equal(new[] { 3000, 5173 }, DevProcesses.SortPorts([5173, 3000, 5173, 0, 70000]));
        Assert.Empty(DevProcesses.SortPorts(null));
    }

    [Fact]
    public void FormatPorts_LimitsLongLists()
    {
        Assert.Equal("—", DevProcesses.FormatPorts(null));
        Assert.Equal("—", DevProcesses.FormatPorts([]));
        Assert.Equal("3000", DevProcesses.FormatPorts([3000]));
        Assert.Equal("1, 2, 3, 4 +1", DevProcesses.FormatPorts([1, 2, 3, 4, 5]));
        Assert.Equal("1, 2 +3", DevProcesses.FormatPorts([1, 2, 3, 4, 5], max: 2));
    }

    [Fact]
    public void FormatMemory_UsesMegabytesAndGigabytes()
    {
        Assert.Equal("—", DevProcesses.FormatMemory(0));
        Assert.Equal("<1 МБ", DevProcesses.FormatMemory(512 * 1024));
        Assert.Equal("120 МБ", DevProcesses.FormatMemory(120L * 1024 * 1024));
        Assert.Equal("1.5 ГБ", DevProcesses.FormatMemory(1536L * 1024 * 1024));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 порт")]
    [InlineData(2, "2 порта")]
    [InlineData(4, "4 порта")]
    [InlineData(5, "5 портов")]
    [InlineData(11, "11 портов")]
    [InlineData(21, "21 порт")]
    [InlineData(22, "22 порта")]
    public void PortsCount_UsesRussianPlural(int count, string expected) =>
        Assert.Equal(expected, DevProcesses.PortsCount(count));

    [Fact]
    public void PortScanner_FindsCurrentProcessListener()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            IReadOnlyDictionary<int, IReadOnlyList<int>> listeners = PortScanner.ListenersByPid();

            Assert.True(listeners.TryGetValue(Environment.ProcessId, out IReadOnlyList<int>? ports),
                "Процесс тестов должен находиться в таблице слушающих сокетов.");
            Assert.Contains(port, ports);
        }
        finally
        {
            listener.Stop();
        }
    }
}
