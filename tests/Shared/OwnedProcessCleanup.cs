using System.Diagnostics;

namespace DAP.Testing;

/// <summary>Owns E2E child-process cleanup on Ctrl+C, process exit and normal disposal.</summary>
internal sealed class OwnedProcessCleanup : IDisposable
{
    private readonly Func<Process?>[] _ownedProcesses;
    private bool _disposed;

    public OwnedProcessCleanup(params Func<Process?>[] ownedProcesses)
    {
        _ownedProcesses = ownedProcesses ?? throw new ArgumentNullException(nameof(ownedProcesses));
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    private void OnProcessExit(object? sender, EventArgs args) => Cleanup();
    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs args) => Cleanup();

    public void Cleanup()
    {
        foreach (var getProcess in _ownedProcesses)
            TryKillProcessTree(getProcess());
    }

    public static void TryKillProcessTree(Process? process)
    {
        if (process is null)
            return;
        try
        {
            if (process.HasExited)
                return;
            process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(5000) && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
        catch (ObjectDisposedException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        Console.CancelKeyPress -= OnCancelKeyPress;
    }
}
