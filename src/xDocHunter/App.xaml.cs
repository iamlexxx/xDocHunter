using System.Windows;
using xDocHunter.Services;

namespace xDocHunter;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Initialize();
    }
}
