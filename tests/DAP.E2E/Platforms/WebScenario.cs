using System.Net.Http;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DAP.TestCRM.Web.E2E;
using DAP.Core.Targets;
using DAP.Core.Guides;
using DAP.Data.Sqlite;
using DAP.Data.Sqlite.Guides;

using System.IO;

namespace DAP.E2E.Platforms;

internal static class WebScenario
{
    public static async Task RunAsync(string[] args)
    {
        const string baseUrl = "http://localhost:5200";
        
        var runOptions = DAP.Testing.E2eRunOptions.Parse(args, "Web");
        var publishedDapDirectory = runOptions.PublishedDapDirectory;
        var guideId = runOptions.GuideId ?? (runOptions.ResetGuide ? string.Empty : throw new ArgumentException("--guide <GuideId> is required."));
        
        if (args.Contains("--reset-guide", StringComparer.OrdinalIgnoreCase))
        {
            var resetOptions = SqliteDatabaseOptions.CreateDefault();
            var resetFactory = new SqliteConnectionFactory(resetOptions);
            await new SqliteDatabaseInitializer(resetFactory).InitializeAsync();
            var resetRepository = new SqliteGuideStepRepository(resetFactory);
            await resetRepository.RenameGuideAsync(
                DapTestCrmGuideSeed.LegacyGuideId,
                DapTestCrmGuideSeed.GuideId,
                DapTestCrmGuideSeed.GuideName);
            var resetSteps = DapTestCrmGuideSeed.CreateSteps();
            await resetRepository.ReplaceStepsAsync(DapTestCrmGuideSeed.GuideId, resetSteps);
            var expectedContexts = DapTestCrmGuideSeed.CreateApplicationContexts();
            await resetRepository.ReplaceApplicationContextsAsync(
                DapTestCrmGuideSeed.GuideId,
                expectedContexts);
        
            var persistedSteps = await resetRepository.GetStepsAsync(DapTestCrmGuideSeed.GuideId);
            var persistedContexts = await resetRepository.GetApplicationContextsAsync(DapTestCrmGuideSeed.GuideId);
        
            if (persistedSteps.Count != resetSteps.Count ||
                persistedSteps.Any(step => !string.Equals(
                    step.ApplicationContextKey,
                    DapTestCrmGuideSeed.ApplicationContextKey,
                    StringComparison.Ordinal)))
                throw new InvalidOperationException(
                    $"Reset verification failed: all {resetSteps.Count} canonical Web Steps must reference Application Context '{DapTestCrmGuideSeed.ApplicationContextKey}'.");
        
            if (persistedContexts.Count != 1)
                throw new InvalidOperationException(
                    $"Reset verification failed: expected exactly one Application Context; found {persistedContexts.Count}.");
        
            var persistedContext = persistedContexts[0];
            var expectedContext = expectedContexts.Single();
            if (!string.Equals(persistedContext.Key, expectedContext.Key, StringComparison.Ordinal) ||
                persistedContext.Runtime != TargetRuntime.Web ||
                persistedContext.Matchers.Count != 1 ||
                persistedContext.Matchers[0].Order != 0 ||
                !string.Equals(persistedContext.Matchers[0].Kind, "UrlHost", StringComparison.Ordinal) ||
                !string.Equals(persistedContext.Matchers[0].Value, "localhost:5200", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Reset verification failed: persisted CRM Application Context does not match the canonical Web definition.");
        
            Console.WriteLine($"Reset Guide '{DapTestCrmGuideSeed.GuideId}' ({resetSteps.Count} steps) in {resetOptions.DatabasePath}");
            Console.WriteLine(
                $"Verified Application Context '{persistedContext.Key}': Runtime={persistedContext.Runtime}, " +
                $"Matcher={persistedContext.Matchers[0].Kind}:{persistedContext.Matchers[0].Value}, " +
                $"Steps={persistedSteps.Count}.");
            return;
        }
        
        var manual = runOptions.Manual;
        var hybrid = runOptions.Hybrid;
        
        static void EnsurePortFree(int port) =>
            DAP.Testing.E2ePortGuard.EnsurePortFree(port,
                $"Port {port} is already in use. Stop the existing process that owns this TestCRM port before starting a new Web E2E/manual run.");
        
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var testCrmProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Web", "DAP.TestCRM.Web.csproj");
        var testCrmBackendProject = Path.Combine(repoRoot, "test-apps", "DAP.TestCRM", "Server", "DAP.TestCRM.Server.csproj");
        var dapAppProject = Path.Combine(repoRoot, "src", "DAP.App", "DAP.App.csproj");
        var nativeHostProject = Path.Combine(repoRoot, "src", "DAP.Runtime.Web.NativeHost", "DAP.Runtime.Web.NativeHost.csproj");
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
        var packagedWebDirectory = packagedDiagnostics ? Path.Combine(diagnosticsRoot!, "TestCRM", "Web") : null;
        var packagedDapDirectory = packagedDiagnostics ? Path.GetFullPath(Path.Combine(diagnosticsRoot!, "..")) : null;
        var webRunRoot = Path.Combine(
            Path.GetTempPath(),
            "DAP",
            "E2E",
            "Web",
            Guid.NewGuid().ToString("N"));
        var backendOutput = Path.Combine(webRunRoot, "Server");
        var webOutput = Path.Combine(webRunRoot, "Web");
        var dapOutput = Path.Combine(webRunRoot, "DAP");
        
        async Task BuildNativeHostAsync()
        {
            Console.WriteLine("Refreshing DAP Native Host for extension-native E2E transport.");
        
            // The registered Native Host loads directly from this project's normal bin
            // directory, so any running host locks the DLLs MSBuild must replace.
            // Terminate stale/current host instances before building, not after.
            foreach (var process in Process.GetProcessesByName("DAP.Runtime.Web.NativeHost"))
            {
                try
                {
                    if (!process.HasExited)
                    {
                        Console.WriteLine($"Stopping DAP Native Host PID {process.Id} before rebuild.");
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(5000))
                            throw new InvalidOperationException(
                                $"DAP Native Host PID {process.Id} did not exit within 5 seconds.");
                    }
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally
                {
                    process.Dispose();
                }
            }
        
            using var build = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{nativeHostProject}\" --nologo --verbosity minimal",
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException("Could not start DAP Native Host build.");
        
            var stdout = build.StandardOutput.ReadToEndAsync();
            var stderr = build.StandardError.ReadToEndAsync();
            await build.WaitForExitAsync();
        
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"DAP Native Host build failed with exit code {build.ExitCode}.{Environment.NewLine}" +
                    $"STDOUT:{Environment.NewLine}{await stdout}{Environment.NewLine}" +
                    $"STDERR:{Environment.NewLine}{await stderr}");
            }
        
            Console.WriteLine("DAP Native Host rebuilt; the extension will reconnect with the current binary.");
        }
        
        async Task BuildIsolatedAsync(string project, string output, string name)
        {
            Directory.CreateDirectory(output);
            Console.WriteLine($"Building {name} into isolated E2E output: {output}");
        
            using var build = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build \"{project}\" --nologo --verbosity minimal --output \"{output}\"",
                WorkingDirectory = repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }) ?? throw new InvalidOperationException($"Could not start isolated build for {name}.");
        
            var stdout = build.StandardOutput.ReadToEndAsync();
            var stderr = build.StandardError.ReadToEndAsync();
            await build.WaitForExitAsync();
        
            if (build.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{name} isolated build failed with exit code {build.ExitCode}.{Environment.NewLine}" +
                    $"STDOUT:{Environment.NewLine}{await stdout}{Environment.NewLine}" +
                    $"STDERR:{Environment.NewLine}{await stderr}");
            }
        }
        
        Process? ownedTestCrmProcess = null;
        Process? ownedTestCrmBackendProcess = null;
        Process? dapProcess = null;
        Process? browserProcess = null;
        ExtensionTestDriver? testDriver = null;
        Task<string>? dapStdOutTask = null;
        var testCrmWebStdOut = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var testCrmWebStdErr = new System.Collections.Concurrent.ConcurrentQueue<string>();
        
        using var ownedProcessCleanup = new DAP.Testing.OwnedProcessCleanup(
            () => dapProcess,
            () => browserProcess,
            () => ownedTestCrmProcess,
            () => ownedTestCrmBackendProcess);
        
        {
            if (!packagedDiagnostics && !File.Exists(testCrmProject))
                throw new FileNotFoundException("TestCRM Web project was not found.", testCrmProject);
            if (!packagedDiagnostics && !File.Exists(testCrmBackendProject))
                throw new FileNotFoundException("TestCRM Server project was not found.", testCrmBackendProject);
            if (!packagedDiagnostics && publishedDapDirectory is null && !File.Exists(dapAppProject))
                throw new FileNotFoundException("DAP.App project was not found.", dapAppProject);
            if (!packagedDiagnostics && !File.Exists(nativeHostProject))
                throw new FileNotFoundException("DAP Native Host project was not found.", nativeHostProject);
            if (publishedDapDirectory is not null && !File.Exists(Path.Combine(publishedDapDirectory, "DAP.exe")))
                throw new FileNotFoundException("Published DAP.exe was not found.", Path.Combine(publishedDapDirectory, "DAP.exe"));
        
            // Fail before building/launching anything if a prior or unrelated process
            // already owns the canonical TestCRM ports. The runner never kills an
            // arbitrary port owner.
            EnsurePortFree(5200);
            EnsurePortFree(5201);
        
            if (!packagedDiagnostics)
            {
                // The Chrome/Edge registration points to the Native Host project's
                // normal bin output. Build that exact binary and terminate stale host
                // processes before opening the browser so the extension must reconnect
                // through the current extension-native transport.
                await BuildNativeHostAsync();
        
                await BuildIsolatedAsync(testCrmBackendProject, backendOutput, "TestCRM Server");
                await BuildIsolatedAsync(testCrmProject, webOutput, "TestCRM Web");
        
                // dotnet build --output does not carry the Web project's static content
                // into this custom isolated output. The E2E host must be a faithful
                // runnable copy, so mirror the source wwwroot into the owned run folder.
                var sourceWebRoot = Path.Combine(Path.GetDirectoryName(testCrmProject)!, "wwwroot");
                var isolatedWebRoot = Path.Combine(webOutput, "wwwroot");
                if (!Directory.Exists(sourceWebRoot))
                    throw new DirectoryNotFoundException($"TestCRM Web root was not found: {sourceWebRoot}");
                Directory.CreateDirectory(isolatedWebRoot);
                foreach (var sourceFile in Directory.EnumerateFiles(sourceWebRoot, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(sourceWebRoot, sourceFile);
                    var destinationFile = Path.Combine(isolatedWebRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                    File.Copy(sourceFile, destinationFile, overwrite: true);
                }
        
                // Manual and Hybrid both run the production DAP Runtime, so the runner requires DAP.exe.
                if (publishedDapDirectory is null)
                    await BuildIsolatedAsync(dapAppProject, dapOutput, "DAP");
            }
        
            var effectiveBackendDirectory = packagedDiagnostics ? packagedServerDirectory! : backendOutput;
            var effectiveWebDirectory = packagedDiagnostics ? packagedWebDirectory! : webOutput;
            var backendDll = Path.Combine(effectiveBackendDirectory, "DAP.TestCRM.Server.dll");
            var webDll = Path.Combine(effectiveWebDirectory, "DAP.TestCRM.Web.dll");
        
            var backendPsi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{backendDll}\"",
                WorkingDirectory = effectiveBackendDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            backendPsi.Environment["ASPNETCORE_URLS"] = "http://localhost:5201";
            ownedTestCrmBackendProcess = Process.Start(backendPsi)
                ?? throw new InvalidOperationException("Could not start TestCRM backend for E2E.");
            ownedTestCrmBackendProcess.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine($"[TestCRM Backend] {e.Data}");
            };
            ownedTestCrmBackendProcess.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.Error.WriteLine($"[TestCRM Backend ERROR] {e.Data}");
            };
            ownedTestCrmBackendProcess.BeginOutputReadLine();
            ownedTestCrmBackendProcess.BeginErrorReadLine();
        
            // Every Web E2E mode is self-contained: the harness owns the TestCRM
            // backend and Web host and cleans up only the processes it started.
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{webDll}\"",
                WorkingDirectory = effectiveWebDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.Environment["ASPNETCORE_URLS"] = baseUrl;
            psi.Environment["TestCrmBackendUrl"] = "http://localhost:5201";
            ownedTestCrmProcess = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start TestCRM Web host for E2E.");
        
            ownedTestCrmProcess.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;
                testCrmWebStdOut.Enqueue(e.Data);
                while (testCrmWebStdOut.Count > 200)
                    testCrmWebStdOut.TryDequeue(out string? _);
                Console.WriteLine($"[TestCRM Web] {e.Data}");
            };
            ownedTestCrmProcess.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data))
                    return;
                testCrmWebStdErr.Enqueue(e.Data);
                while (testCrmWebStdErr.Count > 200)
                    testCrmWebStdErr.TryDequeue(out string? _);
                Console.Error.WriteLine($"[TestCRM Web ERROR] {e.Data}");
            };
            ownedTestCrmProcess.BeginOutputReadLine();
            ownedTestCrmProcess.BeginErrorReadLine();
        
            var crmReadyDeadline = DateTime.UtcNow.AddSeconds(30);
            var crmReady = false;
            using var http = new HttpClient();
        
            while (DateTime.UtcNow < crmReadyDeadline)
            {
                if (ownedTestCrmProcess.HasExited)
                {
                    throw new Exception(
                        $"TestCRM exited before becoming ready. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                        $"STDOUT tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdOut)}{Environment.NewLine}" +
                        $"STDERR tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdErr)}");
                }
        
                try
                {
                    using var response = await http.GetAsync(baseUrl);
        
                    // HTTP success is accepted only while the exact Web-host process
                    // launched by this runner is still alive. This prevents a stale
                    // process on port 5200 from satisfying readiness for a failed launch.
                    if ((int)response.StatusCode < 500 && !ownedTestCrmProcess.HasExited)
                    {
                        crmReady = true;
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                }
        
                await Task.Delay(200);
            }
        
            if (!crmReady)
            {
                if (ownedTestCrmProcess.HasExited)
                {
                    throw new Exception(
                        $"TestCRM exited before becoming ready. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                        $"STDOUT tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdOut)}{Environment.NewLine}" +
                        $"STDERR tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdErr)}");
                }
        
                throw new Exception($"TestCRM did not become ready at {baseUrl} within 30 seconds.");
            }
        
            Console.WriteLine($"Self-contained TestCRM started at {baseUrl} (Web PID {ownedTestCrmProcess.Id}, Backend PID {ownedTestCrmBackendProcess.Id}).");
        }
        
        var harnessStartupTimer=Stopwatch.StartNew();
        var harnessLastMark=TimeSpan.Zero;
        void StartupMark(string stage)
        {
            var now=harnessStartupTimer.Elapsed;
            Console.WriteLine($"[Web runner startup] {now.TotalMilliseconds:F0} ms total (+{(now-harnessLastMark).TotalMilliseconds:F0} ms) - {stage}");
            harnessLastMark=now;
        }
        
        var sessionId = Guid.NewGuid().ToString("N");
        var chromeCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
        };
        var chromeExecutable = chromeCandidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException("Google Chrome was not found.");
        
        // Both Manual and Hybrid get a runner-owned browser-session identity.
        // Manual never receives automation commands; the session exists only so the
        // runner can distinguish intentional browser closure from a DAP failure.
        var browserUrl = $"{baseUrl}?dap-e2e-session={sessionId}";
        browserProcess = Process.Start(new ProcessStartInfo
        {
            FileName = chromeExecutable,
            Arguments = $"--new-window \"{browserUrl}\"",
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Could not start Chrome.");
        StartupMark("Chrome launched with installed DAP extension");
        
        testDriver = new ExtensionTestDriver(sessionId);
        using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await testDriver.ConnectAsync(connectTimeout.Token);
            // Chrome can connect the Extension pipe before the new session tab is registered.
            // Retry only the transient zero-tab result; ambiguity and other errors fail immediately.
            while (true)
            {
                connectTimeout.Token.ThrowIfCancellationRequested();
                try
                {
                    await testDriver.SendAsync(new { type = "testPing" }, connectTimeout.Token);
                    break;
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("DAP test driver expected exactly one session tab for", StringComparison.Ordinal)
                    && ex.Message.Contains("but found 0.", StringComparison.Ordinal))
                {
                    await Task.Delay(100, connectTimeout.Token);
                }
            }
        }
        StartupMark(hybrid
            ? "Extension-native Hybrid test driver connected"
            : "Extension-native Manual browser session connected");
        
        Console.WriteLine($"Web run mode: {(manual ? "manual" : "hybrid")}");
        
        try
        {
        Console.WriteLine("DAP TestCRM representative PeopleSoft-Web scenario");
        Console.WriteLine("Scenario 1: Case status FieldChange + Content iframe replacement");
        Console.WriteLine("Scenario 2: Case validation failure + preservation of unsaved values");
        Console.WriteLine("Scenario 3: Grid rerender/reorder + target re-resolution");
        Console.WriteLine("Scenario 5: CRM tab switching + business context preservation");
        Console.WriteLine("Scenario 6: Conditional target disappearance/reappearance + re-resolution");
        Console.WriteLine("Scenario 7: Cross-frame navigation from Header to Content");
        Console.WriteLine("Scenario 8: Layout shift + target re-resolution");
        Console.WriteLine("Scenario 9: Consecutive server updates + final-state re-resolution");
        Console.WriteLine("Scenario 10: Business context switch + target isolation");
        StartupMark("scenario harness initialized");
        StartupMark("TestCRM browser navigation requested");
        
        // Every Web scenario mode consumes the same persisted production Guide.
        // Both Manual and Hybrid run the production DAP Runtime from Step 1 against the
        // persisted Guide in DAP.db. Target resolution, validation, capture and completion
        // remain Runtime-owned.
        var dapDatabaseOptions=SqliteDatabaseOptions.CreateDefault();
        var dapDbPath=dapDatabaseOptions.DatabasePath;
        var dapFactory=new SqliteConnectionFactory(dapDatabaseOptions);
        await new SqliteDatabaseInitializer(dapFactory).InitializeAsync();
        var dapRepository=new SqliteGuideStepRepository(dapFactory);
        await dapRepository.RenameGuideAsync(
            DapTestCrmGuideSeed.LegacyGuideId,
            DapTestCrmGuideSeed.GuideId,
            DapTestCrmGuideSeed.GuideName);
        var dapSteps=await dapRepository.GetStepsAsync(guideId);
        Console.WriteLine($"DAP persistent guide database: {dapDbPath}");
        StartupMark("persistent DAP guide loaded");
        
        if(dapSteps.Count==0)
            throw new Exception(
                $"DAP Guide '{guideId}' does not exist in the persistent database. " +
                "Initialize/reset the Guide explicitly before running the E2E.");
        var expectedGuideStepCount=DapTestCrmGuideSeed.CreateSteps().Count;
        if(dapSteps.Count!=expectedGuideStepCount)
            throw new Exception(
                $"DAP Guide '{guideId}' must contain exactly {expectedGuideStepCount} Steps for the canonical Web scenario; found {dapSteps.Count}.");
        if(dapSteps.Select(step=>step.Order).Distinct().Count()!=dapSteps.Count
            || dapSteps.Min(step=>step.Order)!=1
            || dapSteps.Max(step=>step.Order)!=dapSteps.Count)
            throw new Exception(
                $"DAP Guide '{guideId}' must have contiguous unique Step orders 1..{dapSteps.Count}.");
        if(dapSteps.Any(step =>
               !(step.Target?.Runtime==TargetRuntime.Web
                 || (step.Target is null
                     && step.Bubble.Placement==BubblePlacement.Center
                     && step.AdvanceMode==StepAdvanceMode.Manual
                     && step.Context is null
                     && step.Validation is null
                     && step.Capture is null
                     && step.CompletionConditions is not { Count: > 0 }))))
            throw new Exception(
                $"DAP Guide '{guideId}' contains a Step that is neither a Web target Step nor a valid centered information Step.");
        
        
        var effectiveDapDirectory = publishedDapDirectory ?? packagedDapDirectory ?? dapOutput;
        var dapStdErrLines=new System.Collections.Concurrent.ConcurrentQueue<string>();
        
        async Task<bool> WaitForHybridGuideStep(GuideStep expected)
        {
            var startMarker=$"[DAP Web guide] starting Step {expected.Order}/{dapSteps.Count} '{expected.Id}'";
        
            // Hybrid mode may stop on a manual learner action for an arbitrary amount
            // of human time. The 5-second regression synchronization timeout must not
            // become a learner-response timeout. A later Runtime Step also proves that
            // this Step was already passed between runner polling intervals.
            while (true)
            {
                if (dapStdErrLines.Any(line=>line.Contains(startMarker,StringComparison.Ordinal)))
                    return true;
        
                var laterStepObserved = dapStdErrLines.Any(line =>
                {
                    const string prefix = "[DAP Web guide] starting Step ";
                    if (!line.StartsWith(prefix, StringComparison.Ordinal))
                        return false;
        
                    var slash = line.IndexOf('/', prefix.Length);
                    return slash > prefix.Length
                        && int.TryParse(line[prefix.Length..slash], out var order)
                        && order > expected.Order;
                });
                if (laterStepObserved)
                    return false;
        
                if (dapProcess is not null && dapProcess.HasExited)
                {
                    if (dapProcess.ExitCode == 0)
                        return false;
        
                    // Closing the E2E-owned browser tab/window intentionally tears down
                    // the production Web transport, which can make DAP exit non-zero.
                    // In Hybrid mode the extension test session gives us an independent
                    // way to distinguish that normal user closure from a real DAP failure.
                    if (hybrid && testDriver is not null)
                    {
                        try
                        {
                            using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                            await testDriver.SendAsync(new { type = "testPing" }, probeTimeout.Token);
                        }
                        catch (Exception ex) when (
                            ex is IOException
                            or InvalidOperationException
                            or OperationCanceledException)
                        {
                            // The Hybrid owner prints the single user-facing closure message
                            // after it leaves Step synchronization.
                            return false;
                        }
                    }
        
                    var startupErrors = dapStdErrLines.ToArray();
                    throw new Exception(
                        $"DAP.exe exited with code {dapProcess.ExitCode} before Runtime activated Step {expected.Order}." +
                        (startupErrors.Length == 0
                            ? " No DAP stderr was captured."
                            : $"{Environment.NewLine}DAP STDERR:{Environment.NewLine}{string.Join(Environment.NewLine, startupErrors)}"));
                }
        
                if (ownedTestCrmProcess is not null && ownedTestCrmProcess.HasExited)
                    throw new Exception($"TestCRM Web host exited before Runtime activated Step {expected.Order}.");
        
                await Task.Delay(100);
            }
        }
        
        var dapExecutable=Path.Combine(effectiveDapDirectory,"DAP.exe");
        if(!File.Exists(dapExecutable))
            throw new Exception($"DAP executable not found at {dapExecutable}");
        
        dapProcess=new Process
        {
            StartInfo=new ProcessStartInfo
            {
                FileName=dapExecutable,
                Arguments=$"--guide {guideId} --mode {(hybrid ? "hybrid" : "manual")}",
                WorkingDirectory=effectiveDapDirectory,
                UseShellExecute=false,
                CreateNoWindow=true,
                RedirectStandardOutput=true,
                RedirectStandardError=true
            }
        };
        dapProcess.StartInfo.Environment["DAP_DATABASE_PATH"]=dapDbPath!;
        if (hybrid)
            dapProcess.StartInfo.Environment["DAP_LEARNER_AUTOMATION"]="1";
        dapProcess.StartInfo.Environment["DAP_WEB_SESSION_ID"]=sessionId;
        var dapStartupTimer=Stopwatch.StartNew();
        if(!dapProcess.Start())
            throw new Exception("DAP.exe process could not be started.");
        StartupMark("DAP.exe process started");
        
        dapStdOutTask=dapProcess.StandardOutput.ReadToEndAsync();
        dapProcess.ErrorDataReceived+=(_,eventArgs)=>
        {
            if(eventArgs.Data is not null)
            {
                dapStdErrLines.Enqueue(eventArgs.Data);
                if (eventArgs.Data.Contains("[DAP Web guide]", StringComparison.Ordinal)
                    || eventArgs.Data.Contains("Exception", StringComparison.OrdinalIgnoreCase)
                    || eventArgs.Data.Contains("Hybrid", StringComparison.OrdinalIgnoreCase)
                    || eventArgs.Data.Contains("error", StringComparison.OrdinalIgnoreCase))
                    Console.WriteLine("[DAP Runtime] " + eventArgs.Data);
            }
        };
        dapProcess.BeginErrorReadLine();
        
        // Synchronize startup on the production Runtime's first active Step.
        await WaitForHybridGuideStep(dapSteps.OrderBy(step => step.Order).First(step => step.IsEnabled));
        dapStartupTimer.Stop();
        Console.WriteLine($"DAP.exe startup to active Step 1: {dapStartupTimer.Elapsed.TotalMilliseconds:F0} ms");
        StartupMark("DAP Runtime reached Step 1");
        
        if (hybrid)
        {
            Console.WriteLine();
            Console.WriteLine("HYBRID WEB RUN: Runtime owns the Guide; persisted automation values fill value controls.");
            Console.WriteLine("Buttons and navigation remain manual learner actions.");
        
            foreach (var step in dapSteps.OrderBy(x => x.Order))
            {
                if (!step.IsEnabled)
                {
                    Console.WriteLine($"HYBRID: skipping disabled persisted Step {step.Order} '{step.Id}'.");
                    continue;
                }
        
                var observed = await WaitForHybridGuideStep(step);
                if (!observed)
                {
                    if (dapProcess is not null && dapProcess.HasExited)
                        break;
        
                    Console.WriteLine($"HYBRID: Runtime already advanced past persisted Step {step.Order} '{step.Id}'.");
                    continue;
                }
        
                // Hybrid values are applied by the production runtime, not the test driver.
        
            }
        
            if (dapProcess is null)
                throw new Exception("DAP.exe process is missing during the hybrid Web run.");
            await dapProcess.WaitForExitAsync();
            if (dapProcess.ExitCode != 0)
            {
                var browserSessionStillOpen = true;
                if (testDriver is not null)
                {
                    try
                    {
                        using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await testDriver.SendAsync(new { type = "testPing" }, probeTimeout.Token);
                    }
                    catch (Exception ex) when (
                        ex is IOException
                        or InvalidOperationException
                        or OperationCanceledException)
                    {
                        browserSessionStillOpen = false;
                    }
                }
        
                if (browserSessionStillOpen)
                    throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} during the hybrid Web run.");
        
                Console.WriteLine("Web browser session closed. Ending the run and cleaning up owned processes.");
                return;
            }
            Console.WriteLine("PASS: hybrid Web Guide completed.");
            return;
        }
        
        if (manual)
        {
            Console.WriteLine();
            Console.WriteLine("MANUAL WEB RUN: Step 1 is ready.");
            Console.WriteLine("Automatic learner actions are disabled. Perform the full Guide manually in the browser.");
            Console.WriteLine("The run will close automatically when DAP completes the Guide or the TestCRM Web host exits.");
            Console.WriteLine("Press Ctrl+C only if you want to stop the run early.");
        
            try
            {
                var dapExit = dapProcess.WaitForExitAsync();
                var webHostExit = ownedTestCrmProcess!.WaitForExitAsync();
        
                // The Chrome launcher PID is not a browser-lifetime signal. Manual uses
                // the same extension session identity as Hybrid, but only to observe
                // whether the runner-owned browser session still exists.
                while (true)
                {
                    var completed = await Task.WhenAny(
                        dapExit,
                        webHostExit,
                        Task.Delay(250));
        
                    if (completed == dapExit)
                    {
                        await dapExit;
                        if (dapProcess.ExitCode == 0)
                        {
                            Console.WriteLine("DAP completed the manual Web Guide. Closing E2E-owned processes.");
                            break;
                        }
        
                        var browserSessionStillOpen = true;
                        try
                        {
                            using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                            await testDriver!.SendAsync(new { type = "testPing" }, probeTimeout.Token);
                        }
                        catch (Exception ex) when (
                            ex is IOException
                            or InvalidOperationException
                            or OperationCanceledException)
                        {
                            browserSessionStillOpen = false;
                        }
        
                        if (!browserSessionStillOpen)
                        {
                            Console.WriteLine("Web browser session closed. Ending the manual run and cleaning up owned processes.");
                            break;
                        }
        
                        throw new Exception($"DAP.exe exited with code {dapProcess.ExitCode} during the manual Web learner run.");
                    }
        
                    if (completed == webHostExit)
                    {
                        await webHostExit;
                        throw new Exception(
                            $"TestCRM Web host exited unexpectedly during the manual learner run. ExitCode={ownedTestCrmProcess.ExitCode}.{Environment.NewLine}" +
                            $"STDOUT tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdOut)}{Environment.NewLine}" +
                            $"STDERR tail:{Environment.NewLine}{string.Join(Environment.NewLine, testCrmWebStdErr)}");
                    }
        
                    try
                    {
                        using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        await testDriver!.SendAsync(new { type = "testPing" }, probeTimeout.Token);
                    }
                    catch (Exception ex) when (
                        ex is IOException
                        or InvalidOperationException
                        or OperationCanceledException)
                    {
                        Console.WriteLine("Web browser session closed. Ending the manual run and cleaning up owned processes.");
                        break;
                    }
                }
            }
            finally
            {
            }
        
            return;
        }
        }
        
        finally
        {
        
            ownedProcessCleanup.Dispose();
        
            DAP.Testing.OwnedProcessCleanup.TryKillProcessTree(dapProcess);
            DAP.Testing.OwnedProcessCleanup.TryKillProcessTree(browserProcess);
            if (testDriver is not null) await testDriver.DisposeAsync();
        
            if (ownedTestCrmProcess is not null)
            {
                try
                {
                    if (!ownedTestCrmProcess.HasExited)
                    {
                        ownedTestCrmProcess.Kill(entireProcessTree: true);
                        ownedTestCrmProcess.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally
                {
                    ownedTestCrmProcess.Dispose();
                }
            }
        
            if (ownedTestCrmBackendProcess is not null)
            {
                try
                {
                    if (!ownedTestCrmBackendProcess.HasExited)
                    {
                        ownedTestCrmBackendProcess.Kill(entireProcessTree: true);
                        ownedTestCrmBackendProcess.WaitForExit(5000);
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                finally
                {
                    ownedTestCrmBackendProcess.Dispose();
                }
            }
        
            try
            {
                if (Directory.Exists(webRunRoot))
                    Directory.Delete(webRunRoot, recursive: true);
            }
            catch (IOException)
            {
                // A hard interruption can leave an isolated run directory temporarily
                // locked. Future runs never reuse it, so it cannot block later builds.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        
        
    }
}
