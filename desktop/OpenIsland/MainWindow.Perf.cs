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
    const double PerfCoreTrackHeight = 22;

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

        PerfState.Text = _perf.Fast ? "1 с" : "3 с";

        PerfCpuText.Text = sampled ? $"{_perf.CpuPercent:0}%" : "—";
        SetPerfBar(PerfCpuBar, sampled ? _perf.CpuPercent : 0);

        IReadOnlyList<double> cores = _perf.CpuCores;
        UpdateCoreBars(cores);
        PerfCores.Visibility = cores.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        PerfCoresEmpty.Visibility = cores.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PerfCoresCount.Text = _perf.CpuCoreCount > 0 ? $"{_perf.CpuCoreCount}×" : "";

        PerfRamText.Text = sampled
            ? $"{_perf.MemoryUsedGb:0.0} / {_perf.MemoryTotalGb:0.0} ГБ · {_perf.MemoryPercent:0}%"
            : "—";
        SetPerfBar(PerfRamBar, sampled ? _perf.MemoryPercent : 0);

        PerfGpuText.Text = _perf.GpuPercent is { } gpu ? $"{gpu:0}%" : "н/д";
        SetPerfBar(PerfGpuBar, _perf.GpuPercent ?? 0);

        PerfDiskText.Text = _perf.DiskPercent is { } disk ? $"{disk:0}%" : "н/д";
        SetPerfBar(PerfDiskBar, _perf.DiskPercent ?? 0);
        PerfDiskSpace.Text = _perf.DiskTotalGb > 0
            ? $"{_perf.DiskFreeGb:0} ГБ свободно из {_perf.DiskTotalGb:0}"
            : "";

        PerfNetText.Text = sampled
            ? $"↓ {PerfRate(_perf.NetDownBps)} · ↑ {PerfRate(_perf.NetUpBps)}"
            : "—";
        PerfTempText.Text = _perf.TemperatureC is { } temperature ? $"{temperature:0}°C" : "н/д";

        // В меню показываем последний известный замер: до первого — пусто, чтобы «0%» не читалось как факт.
        if (MenuPerfCpu != null) MenuPerfCpu.Text = sampled ? $"{_perf.CpuPercent:0}%" : "";
    }

    /// <summary>Столбики по ядрам: создаём ровно столько, сколько пришло (до 16).</summary>
    void UpdateCoreBars(IReadOnlyList<double> cores)
    {
        while (PerfCores.Children.Count > cores.Count)
            PerfCores.Children.RemoveAt(PerfCores.Children.Count - 1);
        while (PerfCores.Children.Count < cores.Count)
        {
            PerfCores.Children.Add(new Rectangle
            {
                Fill = _green,
                RadiusX = 2,
                RadiusY = 2,
                Margin = new Thickness(1, 0, 1, 0),
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 0,
            });
        }

        for (int i = 0; i < cores.Count; i++)
        {
            if (PerfCores.Children[i] is not Rectangle bar) continue;
            double value = Math.Clamp(cores[i], 0, 100);
            bar.Height = Math.Max(2, PerfCoreTrackHeight * value / 100.0);
            bar.Fill = value >= 85 ? _red : value >= 60 ? _orange : _green;
        }
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
