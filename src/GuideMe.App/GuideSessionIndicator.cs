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

    public GuideSessionIndicator(IUiTextProvider texts, Action stop)
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

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        _window.Left = area.Right - _window.ActualWidth - 20;
        _window.Top = area.Bottom - _window.ActualHeight - 20;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _window.Close();
    }
}
