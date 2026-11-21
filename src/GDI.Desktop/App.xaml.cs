using System.IO;
using System.Windows;

namespace GDI.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogCrash(args.ExceptionObject?.ToString() ?? "Unknown AppDomain unhandled exception");
        };

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception?.ToString() ?? "Unknown Dispatcher exception");
            MessageBox.Show(args.Exception?.Message ?? "An unexpected error occurred in the desktop UI.", "GDI Desktop UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogCrash($"App.OnExit called with ExitCode: {e.ApplicationExitCode}. StackTrace:\r\n{Environment.StackTrace}");
        base.OnExit(e);
    }

    private static void LogCrash(string message)
    {
        try
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(Path.Combine(logDir, "desktop_error.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n\r\n");
            File.AppendAllText("desktop_error.log", $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n\r\n");
        }
        catch { }
    }
}

