using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OpenIsland;

/// <summary>
/// Снимок нагрузки ПК: CPU, память, сеть, GPU и температура.
/// Опрос идёт только пока открыта карточка мониторинга (иначе таймер спит),
/// считается на фоновом потоке, а уведомление о готовности приходит в UI-поток.
/// GPU читается через PDH-счётчики, температура — через WMI; там, где данных
/// нет (часто на десктопах), значение помечается как недоступное.
/// </summary>
sealed class PerfService : IDisposable
{
    readonly Dispatcher _dispatcher;
    readonly Timer _timer;

    long _prevIdle, _prevKernel, _prevUser;
    long _prevRx, _prevTx;
    long _prevStamp;
    int _sampling;
    int _ticks;
    bool _cpuPrimed;
    bool _netPrimed;
    bool _active;
    bool _disposed;

    public event Action? Changed;

    public double CpuPercent { get; private set; }
    public double MemoryPercent { get; private set; }
    public double MemoryUsedGb { get; private set; }
    public double MemoryTotalGb { get; private set; }
    public double NetDownBps { get; private set; }
    public double NetUpBps { get; private set; }
    public double? GpuPercent { get; private set; }
    public double? TemperatureC { get; private set; }

    /// <summary>Появился ли хотя бы один полноценный замер (после него имеет смысл показывать числа).</summary>
    public bool HasSample { get; private set; }

    public PerfService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Включает/выключает опрос. Пока карточка закрыта, таймер не тикает вообще.</summary>
    public void SetActive(bool active)
    {
        if (_active == active || _disposed) return;
        _active = active;
        if (active)
        {
            _cpuPrimed = false;
            _netPrimed = false;
            _gpuCollected = false;
            _ticks = 0;
            _timer.Change(0, 1000);
        }
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    void Sample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try
        {
            bool cpu = SampleCpu();
            SampleMemory();
            SampleNetwork();
            if (_ticks % 2 == 0) SampleGpu();
            if (_ticks % 5 == 0) SampleTemperature();
            _ticks++;

            if (!cpu) return; // первый замер только запоминает базовые значения счётчиков
            _dispatcher.BeginInvoke(() => Changed?.Invoke());
        }
        catch (Exception ex)
        {
            App.LogDebug($"Perf: sample failed ({ex.Message}).");
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    // ------------------------------------------------------------------ CPU

    bool SampleCpu()
    {
        if (!GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user)) return false;

        long idleNow = ToLong(idle), kernelNow = ToLong(kernel), userNow = ToLong(user);
        if (!_cpuPrimed)
        {
            _prevIdle = idleNow;
            _prevKernel = kernelNow;
            _prevUser = userNow;
            _cpuPrimed = true;
            return false;
        }

        long idleDelta = idleNow - _prevIdle;
        long totalDelta = (kernelNow - _prevKernel) + (userNow - _prevUser);
        _prevIdle = idleNow;
        _prevKernel = kernelNow;
        _prevUser = userNow;

        if (totalDelta <= 0) return true;
        CpuPercent = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
        HasSample = true;
        return true;
    }

    static long ToLong(FILETIME time) => ((long)time.dwHighDateTime << 32) | (uint)time.dwLowDateTime;

    // ------------------------------------------------------------------ RAM

    void SampleMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return;

        MemoryPercent = status.dwMemoryLoad;
        MemoryTotalGb = status.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
        MemoryUsedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
    }

    // ------------------------------------------------------------- network

    void SampleNetwork()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                IPInterfaceStatistics stats = ni.GetIPStatistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }
        }
        catch (Exception ex)
        {
            App.LogDebug($"Perf: network unavailable ({ex.Message}).");
            return;
        }

        long stamp = Environment.TickCount64;
        if (!_netPrimed)
        {
            _prevRx = rx;
            _prevTx = tx;
            _prevStamp = stamp;
            _netPrimed = true;
            return;
        }

        double seconds = Math.Max((stamp - _prevStamp) / 1000.0, 0.001);
        NetDownBps = Math.Max((rx - _prevRx) / seconds, 0);
        NetUpBps = Math.Max((tx - _prevTx) / seconds, 0);
        _prevRx = rx;
        _prevTx = tx;
        _prevStamp = stamp;
    }

    // -------------------------------------------------------------- GPU (PDH)

    IntPtr _query;
    IntPtr _gpuCounter;
    bool _gpuTried;
    bool _gpuCollected;

    void SampleGpu()
    {
        if (!_gpuTried)
        {
            _gpuTried = true;
            try
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out _query) == 0 &&
                    PdhAddEnglishCounter(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpuCounter) == 0)
                {
                    App.LogDebug("Perf: GPU counters opened.");
                }
                else
                {
                    _query = IntPtr.Zero;
                    App.LogDebug("Perf: GPU counters unavailable.");
                }
            }
            catch (Exception ex)
            {
                App.LogDebug($"Perf: GPU unavailable ({ex.Message}).");
            }
        }

        if (_query == IntPtr.Zero) return;

        try
        {
            if (PdhCollectQueryData(_query) != 0) return;
            if (!_gpuCollected)
            {
                // Счётчику нужны два замера подряд.
                _gpuCollected = true;
                return;
            }

            uint size = 0, count = 0;
            uint result = PdhGetFormattedCounterArray(_gpuCounter, PdhFmtDouble, out size, out count, IntPtr.Zero);
            if (result != PdhMoreData || size == 0) return;

            IntPtr buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PdhGetFormattedCounterArray(_gpuCounter, PdhFmtDouble, out size, out count, buffer) != 0) return;

                double busiest = 0;
                int itemSize = Marshal.SizeOf<PdhCounterItem>();
                for (int i = 0; i < count; i++)
                {
                    PdhCounterItem item = Marshal.PtrToStructure<PdhCounterItem>(buffer + i * itemSize);
                    if (item.FmtValue.Status > 1) continue;
                    busiest = Math.Max(busiest, item.FmtValue.Value);
                }
                GpuPercent = Math.Clamp(busiest, 0, 100);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            App.LogDebug($"Perf: GPU read failed ({ex.Message}).");
        }
    }

    // ------------------------------------------------------ temperature (WMI)

    void SampleTemperature()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using ManagementObjectCollection zones = searcher.Get();
            double hottest = double.MinValue;
            foreach (ManagementBaseObject zone in zones)
            {
                using (zone)
                {
                    if (zone["CurrentTemperature"] is not { } raw) continue;
                    double celsius = Convert.ToDouble(raw) / 10.0 - 273.15;
                    if (celsius > 0 && celsius < 200) hottest = Math.Max(hottest, celsius);
                }
            }
            TemperatureC = hottest > double.MinValue ? Math.Round(hottest) : null;
        }
        catch
        {
            // На большинстве десктопов зона ACPI недоступна — это нормально.
            TemperatureC = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
        if (_query != IntPtr.Zero)
        {
            try { PdhCloseQuery(_query); } catch { }
            _query = IntPtr.Zero;
        }
    }

    // ------------------------------------------------------------- interop

    const uint PdhFmtDouble = 0x00000200, PdhMoreData = 0x800007D2;

    [StructLayout(LayoutKind.Sequential)]
    struct FILETIME { public uint dwLowDateTime, dwHighDateTime; }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
            ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PdhCounterItem
    {
        public IntPtr Name;
        public PdhFormatted FmtValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PdhFormatted { public uint Status; public double Value; }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    static extern uint PdhOpenQuery(string? source, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, EntryPoint = "PdhAddEnglishCounterW")]
    static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, out uint size, out uint count, IntPtr buffer);

    [DllImport("pdh.dll")]
    static extern uint PdhCloseQuery(IntPtr query);
}
