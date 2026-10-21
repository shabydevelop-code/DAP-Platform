using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace GuideMe.Instructor;

internal sealed record TargetWindowSelection(string Runtime, string Property, string Value, string Title);

internal sealed class TargetWindowPicker : Window
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    private readonly BrowserCaptureServer _capture;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _status;
    private TargetWindowSelection? _candidate;
    private readonly Button _confirm;
    private IntPtr _lastExternalWindow;

    public TargetWindowSelection? Selection { get; private set; }

    public TargetWindowPicker(BrowserCaptureServer capture)
    {
        _capture = capture;
        _capture.ClearSelectedBrowserTarget();
        Title = "בחירת חלון יעד — GuideMe";
        Width = 430;
        Height = 205;
        MinWidth = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        FlowDirection = FlowDirection.RightToLeft;
        Background = Brushes.White;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock {
            Text = "בחר חלון יעד",
            FontSize = 18, FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock {
            Text = "עבור לחלון היעד. באתר Web לחץ Alt+Shift+G לאישור הלשונית בתוסף. חזור לכאן ואשר. אין מגבלת זמן.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12)
        });
        _status = new TextBlock {
            Text = "ממתין לחלון יעד...",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 12)
        };
        panel.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _confirm = new Button {
            Content = "בחר חלון זה",
            Padding = new Thickness(14, 7, 14, 7),
            IsEnabled = false,
            Margin = new Thickness(0, 0, 8, 0)
        };
        _confirm.Click += (_, _) => {
            if (_candidate is null) return;
            Selection = _candidate;
            DialogResult = true;
        };
        var cancel = new Button { Content = "ביטול", Padding = new Thickness(14, 7, 14, 7) };
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(_confirm);
        actions.Children.Add(cancel);
        panel.Children.Add(actions);
        Content = panel;
        KeyDown += (_, e) => {
            if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; }
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => RefreshCandidate();
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void RefreshBrowserSelection()
    {
        var selection = _capture.SelectedBrowserTarget;
        if (selection is null)
        {
            _candidate = null;
            _confirm.IsEnabled = false;
            _status.Text = "ממתין לאישור לשונית בדפדפן באמצעות Alt+Shift+G.";
            return;
        }
        _candidate = new TargetWindowSelection("Web", "UrlHost",
            selection.Authority, selection.Title + " — " + selection.Authority);
        _confirm.IsEnabled = true;
        _status.Text = "לשונית אושרה בתוסף: " + _candidate.Title;
    }

    private void RefreshCandidate()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out var pid);
            // Returning to the picker (or Instructor) must not invalidate an
            // already verified target. Only a newly focused external window
            // can replace the selection.
            if (pid == Environment.ProcessId)
            {
                RefreshBrowserSelection();
                return;
            }
            if (hwnd != _lastExternalWindow)
            {
                _lastExternalWindow = hwnd;
                _candidate = null;
                _confirm.IsEnabled = false;
            }
            GetWindowThreadProcessId(_lastExternalWindow, out var targetPid);
            using var process = Process.GetProcessById((int)targetPid);
            var processName = process.ProcessName;
            var titleBuffer = new System.Text.StringBuilder(512);
            GetWindowText(_lastExternalWindow, titleBuffer, titleBuffer.Capacity);
            var title = titleBuffer.ToString();
            if (string.IsNullOrWhiteSpace(title)) title = processName;
            var isBrowser = processName.Equals("chrome", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                processName.Equals("firefox", StringComparison.OrdinalIgnoreCase);
            if (isBrowser)
            {
                RefreshBrowserSelection();
                return;
            }
            else
                _candidate = new TargetWindowSelection("Windows", "ProcessName", processName, title);
            _confirm.IsEnabled = true;
            _status.Text = "חלון מזוהה: " + _candidate.Title;
        }
        catch (ArgumentException)
        {
            _candidate = null;
            _confirm.IsEnabled = false;
            _status.Text = "החלון נסגר. עבור לחלון אחר.";
        }
        catch (InvalidOperationException)
        {
            _candidate = null;
            _confirm.IsEnabled = false;
            _status.Text = "לא ניתן לזהות את החלון. בחר חלון אחר.";
        }
    }
}
