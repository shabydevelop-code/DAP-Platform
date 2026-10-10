using System.Diagnostics;
using System.Windows;

namespace DAP.App;

public partial class App : System.Windows.Application
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
        catch (Exception exception) when (IsClosedWebSession(exception))
        {
            // The user closed the browser tab hosting this learner session.
            // This is an interrupted guide, not a completed guide or a UI error.
            // Exit non-zero so E2E does not report PASS; its session probe will
            // recognize the closed tab and clean up only runner-owned processes.
            exitCode = 2;
            Trace.TraceInformation($"DAP Web session ended because its browser tab was closed: {exception.Message}");
        }
        catch (Exception exception)
        {
            Trace.TraceError($"DAP session terminated unexpectedly: {exception}");
            System.Windows.MessageBox.Show(
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

    private static bool IsClosedWebSession(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("DAP_WEB_TARGET_CLOSED:", StringComparison.Ordinal))
                return true;
            if (current.Message.Contains("DAP test driver expected exactly one session tab for", StringComparison.Ordinal)
                && current.Message.Contains("but found 0", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    public App()
    {
        Startup += OnStartup;
    }
}
