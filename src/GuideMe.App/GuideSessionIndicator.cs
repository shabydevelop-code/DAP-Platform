using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GuideMe.Core.Localization;

namespace GuideMe.App;

/// <summary>
/// Session-owned, non-activating status indicator. Unlike a notification-area
/// icon, this is visible on the desktop and provides an explicit stop action.
/// </summary>
internal sealed class GuideSessionIndicator : IDisposable
{
    private readonly Window _window;
    private bool _disposed;
    private readonly TextBlock _waitingText;
    private bool _waiting;

    public GuideSessionIndicator(IUiTextProvider texts, string guideName, Action stop)
    {
        ArgumentNullException.ThrowIfNull(stop);
        var rtl = texts.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var stopButton = new Button
        {
            Content = texts.Get("Learner.Exit"),
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        stopButton.Click += (_, _) =>
        {
            stopButton.IsEnabled = false;
            stop();
        };
        var caption = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(guideName)
                ? texts.Get("Learner.ActiveStatus")
                : $"{texts.Get("Learner.ActiveStatus")} — {guideName}",
            MaxWidth = 340,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = guideName,
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _waitingText = new TextBlock
        {
            Text = texts.Get("Learner.WaitingForStep"),
            Foreground = new SolidColorBrush(Color.FromRgb(183, 200, 212)),
            FontSize = 12,
            FlowDirection = rtl,
            TextAlignment = texts.IsRightToLeft ? TextAlignment.Right : TextAlignment.Left,
            Visibility = Visibility.Collapsed
        };
        caption.FlowDirection = rtl;
        caption.TextAlignment = texts.IsRightToLeft ? TextAlignment.Right : TextAlignment.Left;
        var captionStack = new StackPanel { Orientation = Orientation.Vertical, FlowDirection = rtl };
        captionStack.Children.Add(caption);
        captionStack.Children.Add(_waitingText);
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = rtl };
        row.Children.Add(captionStack);
        row.Children.Add(stopButton);
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(28, 47, 63)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(93, 117, 134)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(12, 7, 9, 7),
            Child = row
        };
        _window = new Window
        {
            Title = "GuideMe",
            Content = panel,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        // Dragging the caption or background moves the indicator. The Exit
        // button handles its mouse input independently and still stops the guide.
        panel.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || e.Handled) return;
            if (e.OriginalSource is DependencyObject origin)
            {
                for (DependencyObject? node = origin; node is not null;)
                {
                    if (ReferenceEquals(node, stopButton)) return;
                    if (ReferenceEquals(node, panel)) break;
                    node = VisualTreeHelper.GetParent(node);
                }
            }
            try { _window.DragMove(); }
            catch (InvalidOperationException) { /* Mouse was released before drag began. */ }
        };
        _window.Loaded += (_, _) => Reposition();
        _window.Show();
        Reposition();
    }

    // Called only when the learner's existing target/presentation state changes.
    // No extra UIA/DOM queries, polling, or timers.
    public void SetBubbleAvailable(bool available)
    {
        if (_disposed) return;
        void Apply()
        {
            if (_disposed || _waiting == !available) return;
            _waiting = !available;
            _waitingText.Visibility = _waiting ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_window.Dispatcher.CheckAccess()) Apply();
        else _ = _window.Dispatcher.BeginInvoke((Action)Apply);
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        _window.Left = area.Right - _window.ActualWidth - 20;
        _window.Top = area.Bottom - _window.ActualHeight - 20;
    }

    // A target may close while guide execution is still unwinding. Hide the
    // indicator immediately without waiting for the owning session to dispose.
    public void Hide()
    {
        if (_disposed) return;
        if (_window.Dispatcher.CheckAccess())
            _window.Hide();
        else
            _ = _window.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed) _window.Hide();
            });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Close();
    }
}
