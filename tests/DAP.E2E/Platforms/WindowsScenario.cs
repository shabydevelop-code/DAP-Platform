using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Windows.Automation;
using DAP.Core.Guides;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;
using DAP.TestCRM.Windows.E2E;


namespace DAP.E2E.Platforms;

internal static class WindowsScenario
{
    public static async Task RunAsync(string[] args)
    {
        const string appTitle = "DAP Test CRM - Windows";
        const string mainWindowAutomationId = "TestCrmMainWindow";
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var appProject = Path.Combine(root, "test-apps", "DAP.TestCRM", "Windows", "DAP.TestCRM.Windows.csproj");
        var backendProject = Path.Combine(root, "test-apps", "DAP.TestCRM", "Server", "DAP.TestCRM.Server.csproj");
        var dapProject = Path.Combine(root, "src", "DAP.App", "DAP.App.csproj");
        var appProjectDirectory = Path.GetDirectoryName(appProject)!;
        var backendProjectDirectory = Path.GetDirectoryName(backendProject)!;
        var diagnosticsRoot = Environment.GetEnvironmentVariable("DAP_DIAGNOSTICS_ROOT");
        if (!string.IsNullOrWhiteSpace(diagnosticsRoot))
            diagnosticsRoot = Path.GetFullPath(diagnosticsRoot!);
        
        // DAP_DIAGNOSTICS_ROOT can survive in a developer PowerShell session after a
        // packaged diagnostic run. Never let that stale environment value silently turn
        // a repo E2E run into a mixed repo/package run. Packaged mode is valid only when
        // this runner itself is executing from that Diagnostics package.
        var packagedDiagnostics = diagnosticsRoot is not null
            && Path.GetFullPath(AppContext.BaseDirectory).StartsWith(
                Path.Combine(diagnosticsRoot, "Runners") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        var packagedServerDirectory = packagedDiagnostics ? Path.Combine(diagnosticsRoot!, "TestCRM", "Server") : null;
        var packagedWindowsDirectory = packagedDiagnostics ? Path.Combine(diagnosticsRoot!, "TestCRM", "Windows") : null;
        var packagedDapDirectory = packagedDiagnostics ? Path.GetFullPath(Path.Combine(diagnosticsRoot!, "..")) : null;
        
        if (args.Contains("--reset-guide", StringComparer.OrdinalIgnoreCase))
        {
            var options = SqliteDatabaseOptions.CreateDefault();
            var factory = new SqliteConnectionFactory(options);
            await new SqliteDatabaseInitializer(factory).InitializeAsync();
            var repository = new SqliteGuideStepRepository(factory);
            var resetSteps = DapTestCrmWindowsGuideSeed.CreateSteps();
            await repository.ReplaceStepsAsync(DapTestCrmWindowsGuideSeed.GuideId, resetSteps);
            await repository.ReplaceApplicationContextsAsync(
                DapTestCrmWindowsGuideSeed.GuideId,
                DapTestCrmWindowsGuideSeed.CreateApplicationContexts());
        
            await repository.RenameGuideAsync(
                DapTestCrmWindowsGuideSeed.GuideId,
                DapTestCrmWindowsGuideSeed.GuideId,
                DapTestCrmWindowsGuideSeed.GuideName);
        
            Console.WriteLine(
                $"Reset Guide '{DapTestCrmWindowsGuideSeed.GuideId}' " +
                $"({resetSteps.Count} steps) in {options.DatabasePath}");
            return;
        }
        
        var runOptions = DAP.Testing.E2eRunOptions.Parse(args, "Windows");
        var publishedDapDirectory = runOptions.PublishedDapDirectory;
        var guideId = runOptions.GuideId ?? (runOptions.ResetGuide ? string.Empty : throw new ArgumentException("--guide <GuideId> is required."));
        var manual = runOptions.Manual;
        var hybrid = runOptions.Hybrid;
        
        await RunLearnerAsync(manual, hybrid);
        
        async Task RunLearnerAsync(bool manualMode, bool hybridMode)
        {
            var databaseOptions = SqliteDatabaseOptions.CreateDefault();
            var factory = new SqliteConnectionFactory(databaseOptions);
            await new SqliteDatabaseInitializer(factory).InitializeAsync();
            var repository = new SqliteGuideStepRepository(factory);
            var persistedSteps = await repository.GetStepsAsync(guideId);
            var expectedStepCount = DapTestCrmWindowsGuideSeed.CreateSteps().Count;
        
            if (persistedSteps.Count != expectedStepCount)
                throw new InvalidOperationException(
                    $"Guide '{guideId}' contains {persistedSteps.Count} persisted Steps, " +
                    $"but the current seed defines {expectedStepCount}. Run this project once with --reset-guide first.");
        
            var contexts = await repository.GetApplicationContextsAsync(guideId);
            var hasPersistedWindowContext = contexts.Any(context =>
                context.Runtime == DAP.Core.Targets.TargetRuntime.Windows);
        
            Process? backend = null;
            Process? windowsApp = null;
            Process? dap = null;
            var dapStdErrLines = new System.Collections.Concurrent.ConcurrentQueue<string>();
            var runRoot = Path.Combine(Path.GetTempPath(), "DAP", "E2E", "Windows", Guid.NewGuid().ToString("N"));
            var backendOutput = Path.Combine(runRoot, "Server");
            var windowsOutput = Path.Combine(runRoot, "Windows");
            var dapOutput = Path.Combine(runRoot, "DAP");
        
            using var ownedProcessCleanup = new DAP.Testing.OwnedProcessCleanup(
                () => dap, () => windowsApp, () => backend);
        
            try
            {
                EnsurePortFree(5201);
        
                if (!packagedDiagnostics)
                {
                    BuildIsolated(backendProject, backendOutput, "TestCRM Server");
                    BuildIsolated(appProject, windowsOutput, "TestCRM Windows");
                    if (publishedDapDirectory is null)
                        BuildIsolated(dapProject, dapOutput, "DAP");
                }
        
                var effectiveBackendDirectory = packagedDiagnostics ? packagedServerDirectory! : backendOutput;
                var effectiveWindowsDirectory = packagedDiagnostics ? packagedWindowsDirectory! : windowsOutput;
                var effectiveDapDirectory = publishedDapDirectory ?? packagedDapDirectory ?? dapOutput;
                var backendDll = Path.Combine(effectiveBackendDirectory, "DAP.TestCRM.Server.dll");
                var windowsExe = Path.Combine(effectiveWindowsDirectory, "DAP.TestCRM.Windows.exe");
                var dapExe = Path.Combine(effectiveDapDirectory, "DAP.exe");
                if (!File.Exists(dapExe))
                    throw new FileNotFoundException("Published DAP.exe was not found.", dapExe);
        
                backend = StartProcess(
                    "dotnet",
                    $"\"{backendDll}\"",
                    new Dictionary<string, string?> { ["ASPNETCORE_URLS"] = "http://localhost:5201" },
                    workingDirectory: effectiveBackendDirectory);
                await WaitForHttpAsync("http://localhost:5201/api/customers", backend, "TestCRM backend");
        
                windowsApp = StartProcess(windowsExe, string.Empty, workingDirectory: effectiveWindowsDirectory);
                var window = WaitForMainWindow();
                var driver = new WindowsCrmScenarioDriver(windowsApp, window);
        
                dap = StartProcess(
                    dapExe,
                    $"--guide {guideId}" + (hasPersistedWindowContext ? "" : $" --window-automation-id {mainWindowAutomationId}"),
                    hybridMode
                        ? new Dictionary<string, string?> { ["DAP_LEARNER_AUTOMATION"] = "1" }
                        : null,
                    redirectOutput: true,
                    workingDirectory: effectiveDapDirectory);
                dap.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Console.WriteLine($"[DAP] {e.Data}");
                };
                dap.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data))
                        return;
                    dapStdErrLines.Enqueue(e.Data);
                    Console.Error.WriteLine($"[DAP STDERR] {e.Data}");
                };
                dap.BeginOutputReadLine();
                dap.BeginErrorReadLine();
        
                async Task<bool> WaitForRuntimeStepAsync(GuideStep expected)
                {
                    var marker = $"[DAP Windows guide] starting Step {expected.Order}/{persistedSteps.Count} '{expected.Id}'";
                    while (true)
                    {
                        if (dapStdErrLines.Any(line => line.Contains(marker, StringComparison.Ordinal)))
                            return true;
        
                        var laterStepObserved = dapStdErrLines.Any(line =>
                        {
                            const string prefix = "[DAP Windows guide] starting Step ";
                            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                                return false;
                            var slash = line.IndexOf('/', prefix.Length);
                            return slash > prefix.Length
                                && int.TryParse(line[prefix.Length..slash], out var order)
                                && order > expected.Order;
                        });
                        if (laterStepObserved)
                            return false;
        
                        // Target closure is a normal user-requested end of a Manual/Hybrid run.
                        // Check it before DAP because DAP may terminate immediately after losing
                        // the target window, and that exit must not mask the intentional closure.
                        if (windowsApp.HasExited)
                            throw new TargetApplicationClosedException();
        
                        if (dap.HasExited)
                        {
                            if (windowsApp.HasExited)
                                throw new TargetApplicationClosedException();
                            if (dap.ExitCode == 0)
                                return false;
                            throw new Exception($"DAP.exe exited with code {dap.ExitCode} before Runtime activated Step {expected.Order}.");
                        }
        
                        await Task.Delay(100);
                    }
                }
        
                var firstEnabled = persistedSteps.OrderBy(step => step.Order).First(step => step.IsEnabled);
                await WaitForRuntimeStepAsync(firstEnabled);
        
                if (manualMode)
                {
                    Console.WriteLine("MANUAL WINDOWS RUN: Runtime owns the persisted Guide; all learner actions remain manual.");
                    while (!dap.HasExited && !windowsApp.HasExited)
                        await Task.Delay(100);
        
                    // Closing the target application is an intentional end of a Manual run.
                    // Prefer that signal even if DAP exits at nearly the same time because
                    // losing the UIA target can make the production Runtime terminate first.
                    if (windowsApp.HasExited)
                        throw new TargetApplicationClosedException();
        
                    if (dap.HasExited && dap.ExitCode != 0)
                        throw new Exception($"DAP.exe exited with code {dap.ExitCode} during the manual Windows run.");
                    return;
                }
        
                if (!hybridMode)
                    throw new InvalidOperationException("Windows learner mode was not selected.");
        
                Console.WriteLine("HYBRID WINDOWS RUN: Runtime owns the Guide; persisted automation values fill value controls.");
                Console.WriteLine("Buttons, navigation, dialogs, and information confirmations remain manual learner actions.");
        
                foreach (var step in persistedSteps.OrderBy(step => step.Order))
                {
                    if (!step.IsEnabled)
                    {
                        Console.WriteLine($"HYBRID: skipping disabled persisted Step {step.Order} '{step.Id}'.");
                        continue;
                    }
        
                    var observed = await WaitForRuntimeStepAsync(step);
                    if (!observed)
                    {
                        if (dap.HasExited && dap.ExitCode == 0)
                            break;
        
                        Console.WriteLine($"HYBRID: Runtime already advanced past persisted Step {step.Order} '{step.Id}'.");
                        continue;
                    }
        
                    if (string.IsNullOrEmpty(step.AutomationValue) || step.Target is null)
                        continue;
        
                    // Step-start means the Runtime owns the Step, but for text inputs the
                    // Runtime also applies its one-time initial focus. Wait for that focus
                    // setup to finish before Hybrid performs the learner action; otherwise
                    // Step 1 can race and the Runtime can restore focus after Hybrid's TAB.
                    var inputReadyMarker = $"[DAP Windows guide] input ready Step {step.Order} '{step.Id}'";
                    var inputElement = driver.ResolveAutomationValueTarget(step.Target);
                    if (inputElement.Current.ControlType == System.Windows.Automation.ControlType.Edit)
                    {
                        while (!dapStdErrLines.Any(line => line.Contains(inputReadyMarker, StringComparison.Ordinal)))
                        {
                            if (windowsApp.HasExited)
                                throw new TargetApplicationClosedException();
                            if (dap.HasExited)
                                throw new Exception($"DAP.exe exited with code {dap.ExitCode} before input Step {step.Order} became ready.");
                            await Task.Delay(25);
                        }
                    }
        
                    await driver.ApplyAutomationValue(step.Target, step.AutomationValue);
                }
        
                var dapExit = dap.WaitForExitAsync();
                var targetExit = windowsApp.WaitForExitAsync();
                var completed = await Task.WhenAny(dapExit, targetExit);
        
                if (completed == targetExit || windowsApp.HasExited)
                    throw new TargetApplicationClosedException();
        
                await dapExit;
                if (dap.ExitCode != 0)
                {
                    if (windowsApp.HasExited)
                        throw new TargetApplicationClosedException();
                    throw new Exception($"DAP.exe exited with code {dap.ExitCode} during the hybrid Windows run.");
                }
        
                Console.WriteLine("PASS: hybrid Windows Guide completed.");
            }
            catch (TargetApplicationClosedException)
            {
                Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
            }
            catch (Exception) when (windowsApp is not null && windowsApp.HasExited)
            {
                Console.WriteLine("Windows target application closed. Ending the run and cleaning up owned processes.");
            }
            finally
            {
                ownedProcessCleanup.Dispose();
                if (dap is not null) StopOwnedProcessTree(dap);
                if (windowsApp is not null) StopOwnedProcessTree(windowsApp);
                if (backend is not null) StopOwnedProcessTree(backend);
        
                try
                {
                    if (Directory.Exists(runRoot))
                        Directory.Delete(runRoot, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        
        void BuildIsolated(string project, string output, string name)
        {
            Directory.CreateDirectory(output);
            Console.WriteLine($"Building {name} into isolated E2E output: {output}");
        
            using var build = Process.Start(new ProcessStartInfo(
                "dotnet",
                $"build \"{project}\" --nologo --verbosity minimal --output \"{output}\"")
            {
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException($"Could not start isolated build for {name}.");
        
            var stdout = build.StandardOutput.ReadToEndAsync();
            var stderr = build.StandardError.ReadToEndAsync();
            build.WaitForExit();
        
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{name} isolated build failed with exit code {build.ExitCode}.{Environment.NewLine}" +
                    $"STDOUT:{Environment.NewLine}{stdout.GetAwaiter().GetResult()}{Environment.NewLine}" +
                    $"STDERR:{Environment.NewLine}{stderr.GetAwaiter().GetResult()}");
            }
        }
        
        void EnsurePortFree(int port) =>
            DAP.Testing.E2ePortGuard.EnsurePortFree(port,
                $"Port {port} is already in use. Stop the existing TestCRM Server before running this E2E mode.");
        
        Process StartProcess(
            string fileName,
            string arguments,
            IReadOnlyDictionary<string, string?>? environment = null,
            bool redirectOutput = false,
            string? workingDirectory = null)
        {
            var psi = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory ?? root,
                UseShellExecute = false,
                RedirectStandardOutput = redirectOutput,
                RedirectStandardError = redirectOutput
            };
        
            if (environment is not null)
                foreach (var pair in environment)
                    psi.Environment[pair.Key] = pair.Value;
        
            return Process.Start(psi)
                ?? throw new InvalidOperationException($"Could not start process: {fileName} {arguments}");
        }
        
        AutomationElement WaitForMainWindow(int timeout = 5_000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeout)
            {
                var window = AutomationElement.RootElement.FindFirst(
                    TreeScope.Children,
                    new AndCondition(
                        new PropertyCondition(AutomationElement.NameProperty, appTitle),
                        new PropertyCondition(AutomationElement.AutomationIdProperty, mainWindowAutomationId)));
        
                if (window is not null)
                    return window;
        
                Thread.Sleep(100);
            }
        
            throw new TimeoutException("Timed out waiting for Windows TestCRM main window.");
        }
        
        async Task WaitForHttpAsync(string url, Process process, string processName)
        {
            using var http = new HttpClient();
            var deadline = DateTime.UtcNow.AddSeconds(5);
        
            while (DateTime.UtcNow < deadline)
            {
                if (process.HasExited)
                    throw new Exception($"{processName} exited before becoming ready. ExitCode={process.ExitCode}.");
        
                try
                {
                    using var response = await http.GetAsync(url);
                    if ((int)response.StatusCode < 500)
                        return;
                }
                catch (HttpRequestException)
                {
                }
        
                await Task.Delay(200);
            }
        
            throw new TimeoutException($"{processName} did not become ready at {url} within 5 seconds.");
        }
        
        void StopOwnedProcessTree(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(5000) && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(5000) && !process.HasExited)
                            throw new InvalidOperationException(
                                $"E2E-owned process {process.ProcessName} ({process.Id}) did not terminate during cleanup.");
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        
        
    }
}
