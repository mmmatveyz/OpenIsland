using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Threading;

namespace OpenIsland;

/// <summary>
/// Снимок dev-процессов и занятых ими портов для карточки «Процессы и порты».
/// Опрос идёт только пока карточка открыта (иначе таймер спит), перечисление —
/// на фоновом потоке, уведомление о готовности приходит в UI-поток.
/// </summary>
sealed class DevProcessService : IDisposable
{
    const int IntervalMs = 2500;

    readonly Dispatcher _dispatcher;
    readonly Timer _timer;

    int _sampling;
    bool _active;
    bool _disposed;

    public event Action? Changed;

    /// <summary>Отсортированный список процессов (см. <see cref="DevProcesses.Sort"/>).</summary>
    public IReadOnlyList<DevProcessInfo> Items { get; private set; } = [];

    /// <summary>Был ли хотя бы один полный проход (до него список показывается как «сканирую»).</summary>
    public bool HasSample { get; private set; }

    public DevProcessService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _timer = new Timer(_ => Sample(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Включает/выключает опрос: пока карточка закрыта, таймер не тикает вообще.</summary>
    public void SetActive(bool active)
    {
        if (_disposed || _active == active) return;
        _active = active;
        _timer.Change(active ? 0 : Timeout.Infinite, active ? IntervalMs : Timeout.Infinite);
    }

    /// <summary>Внеочередной проход (например, после завершения процесса).</summary>
    public void Refresh()
    {
        if (_disposed || !_active) return;
        _timer.Change(0, IntervalMs);
    }

    void Sample()
    {
        if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
        try
        {
            IReadOnlyDictionary<int, IReadOnlyList<int>> listening = PortScanner.ListenersByPid();
            var items = new List<DevProcessInfo>();

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    string name = process.ProcessName;
                    bool holdsPort = listening.TryGetValue(process.Id, out IReadOnlyList<int>? ports);
                    if (!DevProcesses.IsInteresting(name, holdsPort))
                    {
                        continue;
                    }

                    long memory = 0;
                    try
                    {
                        memory = process.WorkingSet64;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                    {
                        // Рабочий набор недоступен (защищённый процесс) — оставляем 0.
                    }

                    items.Add(new DevProcessInfo(
                        process.Id,
                        DevProcesses.Normalize(name),
                        memory,
                        DevProcesses.SortPorts(holdsPort ? ports : null)));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
                {
                    // Процесс завершился, пока мы его читали.
                }
                finally
                {
                    process.Dispose();
                }
            }

            Items = DevProcesses.Sort(items);
            HasSample = true;
            Notify();
        }
        catch (Exception ex)
        {
            App.LogDebug($"Processes: sample failed ({ex.Message}).");
        }
        finally
        {
            Interlocked.Exchange(ref _sampling, 0);
        }
    }

    /// <summary>Завершает один процесс (без дерева) и возвращает причину отказа для карточки.</summary>
    public (bool Ok, string Error) Kill(int pid)
    {
        if (pid <= 0) return (false, "Некорректный PID.");

        try
        {
            using Process process = Process.GetProcessById(pid);
            process.Kill();
            return (true, "");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return (false, "Процесс уже завершён.");
        }
        catch (Win32Exception)
        {
            return (false, "Не хватает прав, чтобы завершить процесс.");
        }
        catch (Exception ex)
        {
            App.LogDebug($"Processes: kill {pid} failed ({ex.Message}).");
            return (false, "Не удалось завершить процесс.");
        }
    }

    void Notify()
    {
        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (!_disposed) Changed?.Invoke();
            });
        }
        catch
        {
            // Приложение закрывается.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
    }
}
