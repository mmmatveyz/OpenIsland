using System.Windows;
using System.Windows.Shapes;

namespace OpenIsland;

/// <summary>
/// Карточка «Мониторинг ПК». Данные берутся из <see cref="PerfService"/>; опрос
/// идёт только пока карточка открыта (включается из <c>UpdateView</c>).
/// </summary>
public partial class MainWindow
{
    const double PerfBarWidth = 304;

    readonly PerfService _perf;
    bool _perfReady;

    void InitPerf()
    {
        _perfReady = true;
        SyncPerfUI();
    }

    void OnPerfChanged()
    {
        if (_perfReady) SyncPerfUI();
    }

    void PerfRow_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Perf);

    void PerfBack_Click(object sender, RoutedEventArgs e) => ShowPanel(Panel.Menu);

    void SyncPerfUI()
    {
        bool sampled = _perf.HasSample;

        PerfCpuText.Text = sampled ? $"{_perf.CpuPercent:0}%" : "—";
        SetPerfBar(PerfCpuBar, sampled ? _perf.CpuPercent : 0);

        PerfRamText.Text = sampled
            ? $"{_perf.MemoryUsedGb:0.0} / {_perf.MemoryTotalGb:0.0} ГБ · {_perf.MemoryPercent:0}%"
            : "—";
        SetPerfBar(PerfRamBar, sampled ? _perf.MemoryPercent : 0);

        PerfGpuText.Text = _perf.GpuPercent is { } gpu ? $"{gpu:0}%" : "н/д";
        SetPerfBar(PerfGpuBar, _perf.GpuPercent ?? 0);

        PerfNetText.Text = sampled
            ? $"↓ {PerfRate(_perf.NetDownBps)} · ↑ {PerfRate(_perf.NetUpBps)}"
            : "—";
        PerfTempText.Text = _perf.TemperatureC is { } temperature ? $"{temperature:0}°C" : "н/д";

        // В меню показываем последний известный замер: до первого — пусто, чтобы «0%» не читалось как факт.
        if (MenuPerfCpu != null) MenuPerfCpu.Text = sampled ? $"{_perf.CpuPercent:0}%" : "";
    }

    static void SetPerfBar(Rectangle bar, double percent)
        => bar.Width = Math.Round(PerfBarWidth * Math.Clamp(percent, 0, 100) / 100.0);

    static string PerfRate(double bytesPerSecond)
    {
        if (bytesPerSecond < 1024) return $"{bytesPerSecond:0} Б/с";
        if (bytesPerSecond < 1024 * 1024) return $"{bytesPerSecond / 1024:0.0} КБ/с";
        return $"{bytesPerSecond / 1024 / 1024:0.0} МБ/с";
    }
}
