using System.Windows;

namespace DAP.App;

public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var exitCode = await DapApplicationHost.RunAsync(e.Args);
        Shutdown(exitCode);
    }

    public App()
    {
        Startup += OnStartup;
    }
}
