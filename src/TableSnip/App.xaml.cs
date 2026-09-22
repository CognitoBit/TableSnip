using System.Windows;
using System.Windows.Threading;

namespace TableSnip;

public partial class App : Application
{
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

        Theme.Apply();
        Theme.StartListening();

        var window = new MainWindow(startWithSnip: e.Args.Contains("--snip"));
        MainWindow = window;
        window.Show();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "TableSnip",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
