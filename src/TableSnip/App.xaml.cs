using System.Windows;
using System.Windows.Threading;

namespace TableSnip;

public partial class App : Application
{
    /// <summary>Also referenced by the installer (AppMutex) so setup can ask to close a running copy.</summary>
    public const string MutexName = "TableSnip.SingleInstance";
    private const string ShowEventName = "TableSnip.Event.Show";
    private const string SnipEventName = "TableSnip.Event.Snip";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private readonly List<RegisteredWaitHandle> _waits = new();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // Headless mode: TableSnip --file image.png [--out table.tsv] [--gap 1.0] [--json] [--copy]
        if (Array.IndexOf(e.Args, "--file") >= 0)
        {
            int code = await Cli.RunAsync(e.Args);
            Shutdown(code);
            return;
        }

        bool snip = e.Args.Contains("--snip");
        bool tray = e.Args.Contains("--tray");   // start hidden in the tray (used by "open at sign-in")

        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            // A copy is already running: hand over to it (unless this is just the sign-in autostart).
            if (!tray) Signal(snip ? SnipEventName : ShowEventName);
            Shutdown(0);
            return;
        }

        Theme.Apply();
        Theme.StartListening();

        var window = new MainWindow(startWithSnip: snip, startInTray: tray);
        MainWindow = window;
        if (!tray) window.Show();

        Listen(ShowEventName, window.ShowFromTray);
        Listen(SnipEventName, window.SnipFromTray);
    }

    private static void Signal(string name)
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(name);
            ev.Set();
        }
        catch
        {
            // The other copy is still starting up; nothing more we can do.
        }
    }

    private void Listen(string name, Action action)
    {
        var ev = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        _waits.Add(ThreadPool.RegisterWaitForSingleObject(ev, (_, _) => Dispatcher.BeginInvoke(action), null, Timeout.Infinite, false));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "TableSnip",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
