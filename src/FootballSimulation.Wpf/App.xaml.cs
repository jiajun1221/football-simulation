using System.Windows;
using FootballSimulation.Wpf.Services;

namespace FootballSimulation.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.ApplySavedTheme();
        base.OnStartup(e);
    }
}
