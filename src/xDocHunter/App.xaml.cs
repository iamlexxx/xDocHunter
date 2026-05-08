using System.IO;
using System.Windows;
using System.Windows.Threading;
using xDocHunter.Services;

namespace xDocHunter;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "xDocHunter", "crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            WriteLog(ex.ExceptionObject?.ToString());

        DispatcherUnhandledException += (_, ex) =>
        {
            WriteLog(ex.Exception?.ToString());
            ex.Handled = true;
            MessageBox.Show(
                $"Unexpected error:\n\n{ex.Exception?.Message}\n\nDetails written to:\n{LogPath}",
                "xDocHunter — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);
        ThemeManager.Initialize();
    }

    private static void WriteLog(string? text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{text}\n\n");
        }
        catch { }
    }
}
