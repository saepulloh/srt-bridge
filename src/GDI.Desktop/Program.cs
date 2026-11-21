using System;
using System.IO;
using System.Windows;

namespace GDI.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var app = new App();
            app.InitializeComponent();
            var window = new MainWindow();
            app.Run(window);
        }
        catch (Exception ex)
        {
            var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL STARTUP EXCEPTION:\r\n{ex}\r\nInner: {ex.InnerException}";
            try
            {
                File.WriteAllText("desktop_fatal.log", msg);
                var pdata = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GDI", "SRT Embedded", "logs", "desktop_fatal.log");
                Directory.CreateDirectory(Path.GetDirectoryName(pdata)!);
                File.WriteAllText(pdata, msg);
            }
            catch { }

            MessageBox.Show(ex.ToString(), "GDI SRT Embedded — Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
