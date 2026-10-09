using System.Diagnostics;
using System.Windows;

namespace DAP.App;

public partial class App : Application
{
    private async void OnStartup(object sender, StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var exitCode = 1;
        var sessionPid = Environment.ProcessId;
        Trace.TraceInformation($"DAP session started: PID={sessionPid}.");
        try
        {
            exitCode = await DapApplicationHost.RunAsync(e.Args);
        }
        catch (Exception exception)
        {
            Trace.TraceError($"DAP session terminated unexpectedly: {exception}");
            MessageBox.Show(
                exception.Message,
                "DAP - Session Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            // All guide termination paths, including startup exceptions, must
            // release the WPF dispatcher and the loaded assemblies.
            Trace.TraceInformation($"DAP session ending: PID={sessionPid}, exit code={exitCode}.");
            Shutdown(exitCode);
            // This executable hosts one learner session only. After its awaited
            // cleanup completes, do not leave a background process holding DLLs.
            Environment.Exit(exitCode);
        }
    }

    public App()
    {
        Startup += OnStartup;
    }
}
