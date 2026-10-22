using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GuideMe.Core.Localization;

namespace GuideMe.App;

/// <summary>
/// Session-owned, non-activating status indicator. Unlike a notification-area
/// icon, this is visible on the desktop and provides an explicit stop action.
/// </summary>
internal sealed class GuideSessionIndicator : IDisposable
{
    private readonly Window _window;
    private readonly DispatcherTimer _timer;
    private readonly Func<bool> _bubbleVisible;
    private bool _disposed;
    private readonly TimeSpan _showDelay;
    private DateTimeOffset? _noBubbleSince;

    public GuideSessionIndicator(string guideId, IUiTextProvider texts, Action stop,
        Func<bool>? bubbleVisible = null, TimeSpan? showDelay = null)
    {
        ArgumentNullException.ThrowIfNull(stop);
        _bubbleVisible = bubbleVisible ?? (() => false);
        _showDelay = showDelay ?? TimeSpan.Zero;
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
            Text = texts.Get("Learner.ActiveStatus"),
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = rtl };
        row.Children.Add(caption);
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
        _window.Loaded += (_, _) => Reposition();
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _timer.Tick += (_, _) => RefreshVisibility();
        _timer.Start();
        RefreshVisibility();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        _window.Left = area.Right - _window.ActualWidth - 20;
        _window.Top = area.Bottom - _window.ActualHeight - 20;
    }

    private void RefreshVisibility()
    {
        if (_disposed) return;
        bool visible;
        try { visible = !_bubbleVisible(); }
        catch { visible = true; }
        if (!visible)
        {
            _noBubbleSince = null;
            if (_window.IsVisible) _window.Hide();
            return;
        }

        _noBubbleSince ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - _noBubbleSince < _showDelay) return;
        if (visible)
        {
            if (!_window.IsVisible) _window.Show();
            Reposition();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _window.Close();
    }
}
