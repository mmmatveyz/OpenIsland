using System.IO;
using System.Windows;

namespace DynamicIsland;

public partial class App : Application
{
    const string InstanceName = "OpenIsland.SingleInstance";
    static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenIsland", "logs");
    static readonly string DebugLog = Path.Combine(LogDirectory, "app.log");
    static readonly object LogGate = new();
    static readonly TimeSpan PredecessorExitTimeout = TimeSpan.FromSeconds(10);

    Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        LogDebug($"App.OnStartup called. Args: {string.Join(" ", e.Args)}");

        if (e.Args.Contains("--selftest"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            _ = RunSelfTestAsync();
            return;
        }

        _mutex = new Mutex(true, InstanceName, out bool isOnlyInstance);
        LogDebug($"Mutex acquired? {isOnlyInstance}");
        if (!isOnlyInstance && e.Args.Contains(Updater.RestartedFlag)) isOnlyInstance = WaitForPredecessor(_mutex);
        if (!isOnlyInstance)
        {
            LogDebug("Exiting because isOnlyInstance is false (already running instance).");
            Shutdown();
            return;
        }
        Updater.CleanUp();

        base.OnStartup(e);

        Exit += (_, exitArgs) =>
        {
            LogDebug($"App.Exit fired. ExitCode: {exitArgs.ApplicationExitCode}");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogDebug($"DispatcherUnhandledException: {args.Exception}");
            Log(args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogDebug($"AppDomain.UnhandledException: {args.ExceptionObject}");
            if (args.ExceptionObject is Exception ex) Log(ex);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogDebug($"UnobservedTaskException: {args.Exception}");
            args.SetObserved();
        };

        try
        {
            LogDebug("Instantiating MainWindow...");
            var win = new MainWindow();
            win.Closed += (_, _) => LogDebug("MainWindow was Closed!");
            LogDebug("Showing MainWindow...");
            win.Show();
            LogDebug("MainWindow.Show() completed.");
        }
        catch (Exception ex)
        {
            LogDebug($"Exception during MainWindow creation/show: {ex}");
            Log(ex);
            Shutdown();
        }
    }

    public static void LogDebug(string message)
    {
        try
        {
            lock (LogGate)
            {
                Directory.CreateDirectory(LogDirectory);
                FileInfo log = new(DebugLog);
                if (log.Exists && log.Length > 1_000_000)
                {
                    File.Move(DebugLog, DebugLog + ".old", overwrite: true);
                }
                File.AppendAllText(DebugLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\r\n");
            }
        }
        catch { }
    }

    public static void Log(Exception ex)
    {
        try
        {
            // Единый ротируемый журнал: не дублируем в %TEMP%, чтобы файл не рос без ограничений.
            LogDebug($"LOG ERROR: {ex}");
        }
        catch { }
    }

    static bool WaitForPredecessor(Mutex mutex)
    {
        try { return mutex.WaitOne(PredecessorExitTimeout); }
        catch (AbandonedMutexException) { return true; }
    }

    static async Task RunSelfTestAsync()
    {
        int code = await OpenCodeSelfTest.RunAsync();
        LogDebug($"SELFTEST: finished with code {code}.");
        Current.Shutdown(code);
    }
}
