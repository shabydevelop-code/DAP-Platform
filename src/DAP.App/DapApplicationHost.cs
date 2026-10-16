using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using DAP.Data.Sqlite;
using DAP.App.Localization;
using DAP.Core.Localization;
using DAP.Data.Sqlite.Guides;
using DAP.Runtime.Web.Browser;
using DAP.Runtime.Web.Learner;
using DAP.Runtime.Windows.Bubbles;
using DAP.Runtime.Windows.Learner;
using DAP.Runtime.Windows.Targets;

namespace DAP.App;

public static class DapApplicationHost
{
    private static void StartupMark(Stopwatch timer, string stage)
        => Console.Error.WriteLine($"[DAP startup] {timer.Elapsed.TotalMilliseconds:F0} ms - {stage}");

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var startup = Stopwatch.StartNew();
        StartupMark(startup, "host entered");

        IUiTextProvider texts = JsonUiTextProvider.LoadFromApplicationDirectory();
        StartupMark(startup, $"localization loaded ({texts.Language})");

        var options = DapLaunchOptions.Parse(args, texts);
        if (options is null)
            return 2;
        StartupMark(startup, "launch options parsed");

        if (options.Mode == DapLaunchMode.ValidateV5)
        {
            try
            {
                var plan = await GuideV5ExecutionCompiler.CompileAsync(
                    Path.GetFullPath(options.V5DatabasePath!), options.GuideId!, cancellationToken);
                Console.WriteLine($"V5 validation succeeded: {options.GuideId}; {plan.Steps.Count} steps; {plan.Contexts.Count} contexts.");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var reportPath = Path.Combine(Path.GetTempPath(), "GuideMe-V5-validation-error.txt");
                await File.WriteAllTextAsync(reportPath,
                    $"Guide: {options.GuideId}{Environment.NewLine}{ex}",
                    cancellationToken);
                Console.Error.WriteLine($"V5 validation failed for '{options.GuideId}': {ex}");
                Console.Error.WriteLine($"Diagnostic report: {reportPath}");
                return 7;
            }
        }


        // One active Guide execution per Windows logon session, across processes.
        // A named semaphore is used because an async run can resume on another thread.
        Semaphore? guideGate = null;
        var guideGateAcquired = false;
        if (options.Mode != DapLaunchMode.InfrastructureCheck)
        {
            guideGate = new Semaphore(1, 1, @"Local\DAP.ActiveGuide");
            guideGateAcquired = guideGate.WaitOne(0);
            if (!guideGateAcquired)
            {
                guideGate.Dispose();
                System.Windows.MessageBox.Show(
                    texts.Get("Learner.GuideAlreadyRunning"),
                    texts.Get("Learner.WindowTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return 6;
            }
        }
        try
        {



        // V5 is the only learner persistence format. Do not initialize or
        // attempt an implicit migration of a legacy production database.
        var databaseOptions = SqliteDatabaseOptions.CreateV5Default();
        var activeDatabasePath = options.V5DatabasePath is null
            ? databaseOptions.DatabasePath
            : Path.GetFullPath(options.V5DatabasePath);
        // A V5 launch must never open the old production schema, including when
        // DAP_DATABASE_PATH or --v5-db points to the legacy default file.
        var legacyDatabasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "GuideMe", "Data", "GuideMe.db");
        if (string.Equals(activeDatabasePath, legacyDatabasePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Refusing to load the legacy GuideMe database as V5. Use a separately validated V5 database.");
        if (options.Mode != DapLaunchMode.InfrastructureCheck && !File.Exists(activeDatabasePath))
            throw new FileNotFoundException(
                "GuideMe V5 database not found. Provision a validated V5 database before launching a guide.",
                activeDatabasePath);
        StartupMark(startup, $"V5 database selected ({activeDatabasePath})");

        if (options.Mode == DapLaunchMode.InfrastructureCheck)
        {
            var diagnosticsPath = Path.Combine(
                Path.GetTempPath(),
                "DAP",
                "dap-check.txt");

            Directory.CreateDirectory(Path.GetDirectoryName(diagnosticsPath)!);
            await File.WriteAllTextAsync(
                diagnosticsPath,
                $"DAP.exe ready.{Environment.NewLine}Database: {activeDatabasePath}{Environment.NewLine}UTC: {DateTimeOffset.UtcNow:O}{Environment.NewLine}",
                cancellationToken);

            System.Windows.MessageBox.Show(
                texts.Format("App.InfrastructureReady", activeDatabasePath, diagnosticsPath),
                texts.Get("App.InfrastructureCheckTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return 0;
        }

        var v5 = await GuideV5ExecutionCompiler.CompileAsync(
            activeDatabasePath, options.GuideId!, cancellationToken);
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps = v5.Steps;
        IReadOnlyList<DAP.Core.Guides.GuideApplicationContext> applicationContexts = v5.Contexts;
        StartupMark(startup, $"guide loaded ({steps.Count} steps, {applicationContexts.Count} application contexts)");
        if (steps.Count == 0)
        {
            System.Windows.MessageBox.Show(
                texts.Format("Learner.GuideHasNoSteps", options.GuideId!),
                texts.Get("Learner.WindowTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return 5;
        }

        var enabledTargetRuntimes = steps
            .Where(step => step.IsEnabled && step.Target is not null)
            .Select(step => step.Target!.Runtime)
            .Distinct()
            .ToArray();

        if (enabledTargetRuntimes.Length == 2 &&
            enabledTargetRuntimes.Contains(DAP.Core.Targets.TargetRuntime.Web) &&
            enabledTargetRuntimes.Contains(DAP.Core.Targets.TargetRuntime.Windows))
            return await RunMixedAsync(options, steps, applicationContexts, texts, startup, cancellationToken);

        if (enabledTargetRuntimes.Length != 1)
            throw new InvalidOperationException(
                $"Guide '{options.GuideId}' must currently contain enabled targets for exactly one Runtime; found {enabledTargetRuntimes.Length}.");

        return enabledTargetRuntimes[0] switch
        {
            DAP.Core.Targets.TargetRuntime.Windows => await RunWindowsAsync(
                options, steps, applicationContexts, texts, startup, cancellationToken),
            DAP.Core.Targets.TargetRuntime.Web => await RunWebAsync(
                options, steps, applicationContexts, texts, startup, cancellationToken),
            _ => throw new NotSupportedException(
                $"Guide '{options.GuideId}' uses unsupported Runtime '{enabledTargetRuntimes[0]}'.")
        };

        }
        finally
        {
            if (guideGateAcquired)
                guideGate!.Release();
            guideGate?.Dispose();
        }
    }

    private static async Task<int> RunMixedAsync(
        DapLaunchOptions options,
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        IReadOnlyList<DAP.Core.Guides.GuideApplicationContext> applicationContexts,
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {
        var windowsContexts = applicationContexts
            .Where(context => context.Runtime == DAP.Core.Targets.TargetRuntime.Windows)
            .ToDictionary(context => context.Key, StringComparer.Ordinal);
        if (windowsContexts.Count == 0)
            throw new InvalidOperationException("Mixed Guide requires persisted Windows application contexts.");

        using var browser = new ExtensionWebBrowserAdapter(texts);
        browser.ConfigureApplicationContexts(applicationContexts);
        using var stopped = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopped.Token);
        using var tray = new GuideSessionTray(options.GuideId!, texts, () => stopped.Cancel());
        browser.StopRequested += () => stopped.Cancel();
        browser.TargetTabClosed += () => stopped.Cancel();
        TaskCompletionSource<bool>? pendingWebActivation = null;
        browser.TargetWindowActivationRequested += title =>
        {
            var activation = Task.Run(() => ActivateChromeWindowAsync(title, linked.Token));
            var pending = pendingWebActivation;
            if (pending is not null)
                _ = activation.ContinueWith(task =>
                {
                    if (task.IsFaulted) pending.TrySetException(task.Exception!.GetBaseException());
                    else if (task.IsCanceled) pending.TrySetCanceled();
                    else pending.TrySetResult(true);
                }, TaskScheduler.Default);
        };

        var web = new AdapterWebGuideRuntime(
            new WebGuideStepRuntime(browser), browser);
        var bubbles = new WindowsBubblePresenter(texts);
        var windows = new WindowsGuideRuntime(new WindowsTargetResolver(), bubbles);

        AutomationElement ResolveWindow(DAP.Core.Guides.GuideStep step)
        {
            if (string.IsNullOrWhiteSpace(step.ApplicationContextKey) ||
                !windowsContexts.TryGetValue(step.ApplicationContextKey, out var context))
                throw new InvalidOperationException(
                    $"Mixed Guide Windows Step '{step.Id}' requires a persisted Windows application context.");
            return new WindowsApplicationContextResolver().Resolve(context);
        }

        var windowsAdapter = new DAP.Core.Guides.DelegateGuideStepAdapter(
            DAP.Core.Targets.TargetRuntime.Windows, "Windows",
            async (step, index, total, plan, token) =>
            {
                var root = ResolveWindow(step);
                await windows.CreateStepAdapter(root).ExecuteAsync(step, index, total, plan, token);
            },
            async (step, total, token) =>
            {
                var root = ResolveWindow(step);
                await windows.CreateStepAdapter(root).ShowCenteredInformationAsync(step, total, token);
            },
            (step, plan, token) =>
                windows.CreateStepAdapter(ResolveWindow(step)).CaptureAsync(step, plan, token));

        try
        {
            StartupMark(startup, "Mixed Web/Windows guide runtime starting");
            await new DAP.Core.Guides.GuideExecutionEngine().RunAsync(
                steps, new DAP.Core.Guides.IGuideStepAdapter[] { web.CreateStepAdapter(), windowsAdapter },
                DAP.Core.Targets.TargetRuntime.Web, linked.Token, applicationContexts,
                async (step, runtime, token) =>
                {
                    if (runtime == DAP.Core.Targets.TargetRuntime.Windows)
                    {
                        var targetWindow = ResolveWindow(step);
                        // UIA focus is the effective fallback for a Windows transition.
                        // Apply it immediately instead of waiting through five seconds of
                        // repeated foreground requests (which flash the taskbar).
                        try
                        {
                            targetWindow.SetFocus();
                        }
                        catch (ElementNotAvailableException)
                        {
                        }
                        catch (InvalidOperationException)
                        {
                        }
                        if (GetForegroundWindow() != new IntPtr(targetWindow.Current.NativeWindowHandle))
                            await ActivateWindowsTargetAsync(targetWindow, token);
                        if (GetForegroundWindow() != new IntPtr(targetWindow.Current.NativeWindowHandle))
                        {
                            try { targetWindow.SetFocus(); }
                            catch (ElementNotAvailableException) { }
                            catch (InvalidOperationException) { }
                        }
                        return;
                    }

                    // The extension resolves the exact persisted Web context and selects
                    // its tab. The host then requests foreground activation of that window.
                    var activation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    pendingWebActivation = activation;
                    try
                    {
                        await browser.ActivateContextWindowAsync(step, token);
                        await activation.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                    }
                    finally
                    {
                        pendingWebActivation = null;
                    }
                });
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            Console.Error.WriteLine("[DAP diagnostic] Mixed guide stopped.");
        }
        finally
        {
            await bubbles.HideAsync();
            if (!linked.IsCancellationRequested)
                await browser.HideBubbleAsync(CancellationToken.None);
        }
        return 0;
    }

    private static async Task<int> RunWindowsAsync(
        DapLaunchOptions options,
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        IReadOnlyList<DAP.Core.Guides.GuideApplicationContext> applicationContexts,
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {
        var windowsContexts = applicationContexts.Where(context => context.Runtime == DAP.Core.Targets.TargetRuntime.Windows).ToArray();
        AutomationElement window;
        if (windowsContexts.Length == 1)
        {
            try
            {
                window = new WindowsApplicationContextResolver().Resolve(windowsContexts[0]);
            }
            catch (WindowsApplicationContextMatchException ex)
            {
                Console.Error.WriteLine($"[DAP diagnostic] Windows application context resolution failed: {ex}");
                System.Windows.MessageBox.Show(
                    texts.Get("App.TargetWindowNotFoundMessage"),
                    texts.Get("App.TargetWindowNotFoundTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return 4;
            }
        }
        else if (windowsContexts.Length > 1)
            throw new NotSupportedException("Multiple Windows application contexts require per-step context resolution.");
        else if (!string.IsNullOrWhiteSpace(options.WindowAutomationId))
            window = await WaitForWindowAsync(options.WindowAutomationId, cancellationToken);
        else
            throw new InvalidOperationException("Windows Guide requires a persisted application context or --window-automation-id.");
        StartupMark(startup, "Windows target window resolved");
        await ActivateWindowsTargetAsync(window, cancellationToken);
        StartupMark(startup, "Windows target window activated");

        // Observe the native window independently of UI Automation: UIA providers
        // can throw while their owning application is shutting down.
        var targetHandle = new IntPtr(window.Current.NativeWindowHandle);
        using var targetClosed = new CancellationTokenSource();
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, targetClosed.Token);
        var monitor = MonitorWindowsTargetAsync(targetHandle, targetClosed, runCancellation.Token);

        var resolver = new WindowsTargetResolver();
        var bubbles = new WindowsBubblePresenter(texts);
        using var userStopped = new CancellationTokenSource();
        using var tray = new GuideSessionTray(options.GuideId!, texts, () => userStopped.Cancel());
        using var stopRegistration = userStopped.Token.Register(() => runCancellation.Cancel());
        var runtime = new WindowsGuideRuntime(resolver, bubbles);

        try
        {
            StartupMark(startup, "Windows guide runtime starting");
            await runtime.RunAsync(window, steps, runCancellation.Token);
        }
        catch (OperationCanceledException) when (userStopped.IsCancellationRequested)
        {
            Console.Error.WriteLine("[DAP diagnostic] Windows assistance stopped by user.");
        }
        catch (OperationCanceledException) when (targetClosed.IsCancellationRequested)
        {
            Console.Error.WriteLine("[DAP diagnostic] Windows target closed; guide stopped normally.");
        }
        catch (Exception ex) when (!IsWindow(targetHandle))
        {
            // A UIA provider may fail before the monitor's next poll.
            Console.Error.WriteLine($"[DAP diagnostic] Windows target closed during UI automation: {ex}");
        }
        finally
        {
            runCancellation.Cancel();
            try { await monitor; }
            catch (OperationCanceledException) when (runCancellation.IsCancellationRequested) { }
            await bubbles.HideAsync();
        }

        return 0;
    }

    private static async Task MonitorWindowsTargetAsync(IntPtr handle, CancellationTokenSource targetClosed, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!IsWindow(handle))
            {
                Console.Error.WriteLine("[DAP diagnostic] Windows target window handle is no longer valid.");
                targetClosed.Cancel();
                return;
            }
            await Task.Delay(100, cancellationToken);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    // Try the normal Win32 activation first. If the foreground process owns
    // another input queue, temporarily join that queue for the activation call.
    // No synthetic keystrokes or mouse events are sent.
    private static bool RequestForegroundActivation(IntPtr target, IntPtr foreground)
    {
        if (SetForegroundWindow(target)) return true;
        if (foreground == IntPtr.Zero || foreground == target) return false;

        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(target, out _);
        var callerThread = GetCurrentThreadId();
        if (foregroundThread == 0 || targetThread == 0) return false;

        // Attach both input queues to the currently foreground queue for the
        // duration of this request. The prior implementation attached only the
        // caller, leaving the target window on an independent queue.
        var callerAttached = foregroundThread != callerThread &&
            AttachThreadInput(callerThread, foregroundThread, true);
        var targetAttached = foregroundThread != targetThread &&
            AttachThreadInput(targetThread, foregroundThread, true);
        try
        {
            if (foregroundThread != callerThread && !callerAttached)
            if (foregroundThread != targetThread && !targetAttached)
            if ((foregroundThread != callerThread && !callerAttached) ||
                (foregroundThread != targetThread && !targetAttached))
                return false;

            var accepted = SetForegroundWindow(target);
            return accepted;
        }
        finally
        {
            if (targetAttached)
                AttachThreadInput(targetThread, foregroundThread, false);
            if (callerAttached)
                AttachThreadInput(callerThread, foregroundThread, false);
        }
    }

    private static Task ActivateWindowAsync(IntPtr handle, string label, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (handle == IntPtr.Zero || !IsWindow(handle))
            throw new InvalidOperationException($"Target {label} window is not available.");
        if (GetForegroundWindow() == handle)
            return Task.CompletedTask;
        if (IsIconic(handle))
            ShowWindow(handle, 9);
        // Avoid repeated requests that flash the taskbar.
        RequestForegroundActivation(handle, GetForegroundWindow());
        return Task.CompletedTask;
    }

    private static Task ActivateWindowsTargetAsync(AutomationElement target, CancellationToken cancellationToken)
        => ActivateWindowAsync(new IntPtr(target.Current.NativeWindowHandle), "Windows", cancellationToken);

    private static async Task<AutomationElement> WaitForWindowAsync(
        string automationId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        var condition = new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            automationId);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var matches = AutomationElement.RootElement
                .FindAll(TreeScope.Children, condition)
                .Cast<AutomationElement>()
                .ToArray();

            if (matches.Length == 1)
                return matches[0];

            if (matches.Length > 1)
                throw new InvalidOperationException(
                    $"DAP.exe found {matches.Length} Windows target windows with AutomationId '{automationId}'; exactly one is required.");

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException(
            $"DAP.exe did not find a Windows target window with AutomationId '{automationId}' within 30 seconds.");
    }

    private static async Task<int> RunWebAsync(
        DapLaunchOptions options,
        IReadOnlyList<DAP.Core.Guides.GuideStep> steps,
        IReadOnlyList<DAP.Core.Guides.GuideApplicationContext> applicationContexts,
        IUiTextProvider texts,
        Stopwatch startup,
        CancellationToken cancellationToken)
    {

        using var browserAdapter = new ExtensionWebBrowserAdapter(texts);
        browserAdapter.ConfigureApplicationContexts(applicationContexts);
        using var targetClosed = new CancellationTokenSource();
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, targetClosed.Token);
        using var webUserStopped = new CancellationTokenSource();
        using var tray = new GuideSessionTray(options.GuideId!, texts, () => { webUserStopped.Cancel(); runCancellation.Cancel(); });
        browserAdapter.StopRequested += () => { webUserStopped.Cancel(); runCancellation.Cancel(); };
        browserAdapter.TargetTabClosed += () =>
        {
            Console.Error.WriteLine("[DAP diagnostic] TargetTabClosed received; cancelling Web guide.");
            targetClosed.Cancel();
        };
        browserAdapter.TargetWindowActivationRequested += title => _ = Task.Run(() => ActivateChromeWindowAsync(title, runCancellation.Token));
        var stepRuntime = new WebGuideStepRuntime(browserAdapter);
        var guideRuntime = new AdapterWebGuideRuntime(stepRuntime, browserAdapter);
        StartupMark(startup, "Web extension adapter composition root created");

        try
        {
            StartupMark(startup, "Web adapter guide runtime starting");
            await guideRuntime.RunAsync(steps, runCancellation.Token);
            Console.Error.WriteLine("[DAP diagnostic] Web guide runtime completed normally.");
        }
        catch (OperationCanceledException) when (webUserStopped.IsCancellationRequested)
        {
            Console.Error.WriteLine("[DAP diagnostic] Web assistance stopped by user.");
        }
        catch (OperationCanceledException) when (targetClosed.IsCancellationRequested)
        {
            Console.Error.WriteLine("[DAP diagnostic] Web guide cancelled by target-tab-closed event; exit code 2.");
            return 2;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Error.WriteLine("[DAP diagnostic] Web guide cancelled externally.");
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DAP diagnostic] Web guide failed: {ex}");
            throw;
        }
        finally
        {
            if (!targetClosed.IsCancellationRequested)
                await browserAdapter.HideBubbleAsync(CancellationToken.None);
        }

        return 0;
    }

    // Chrome's extension window ID is not a Win32 HWND. Match the selected tab
    // against visible Chrome top-level window captions and refuse ambiguity.
    private static async Task ActivateChromeWindowAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            var matches = new List<IntPtr>();
            EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window)) return true;
                var className = new StringBuilder(128);
                if (GetClassName(window, className, className.Capacity) == 0 ||
                    className.ToString() != "Chrome_WidgetWin_1") return true;
                GetWindowThreadProcessId(window, out var processId);
                try
                {
                    using var process = Process.GetProcessById((int)processId);
                    if (!string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (ArgumentException) { return true; }
                catch (InvalidOperationException) { return true; }
                catch (System.ComponentModel.Win32Exception) { return true; }
                var caption = new StringBuilder(1024);
                GetWindowText(window, caption, caption.Capacity);
                var value = caption.ToString();
                if (value == title || value.StartsWith(title + " - ", StringComparison.Ordinal))
                    matches.Add(window);
                return true;
            }, IntPtr.Zero);

            if (matches.Count != 1)
            {
                throw new InvalidOperationException($"Expected exactly one Chrome window matching active tab; found {matches.Count}.");
            }

            await ActivateWindowAsync(matches[0], "Web", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

}
