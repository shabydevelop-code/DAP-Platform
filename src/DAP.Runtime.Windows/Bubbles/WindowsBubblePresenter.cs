using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DAP.Core.Guides;

namespace DAP.Runtime.Windows.Bubbles;

public sealed class WindowsBubblePresenter
{
    private const double PointerSpace = 16d;
    private const double TargetGap = 10d;

    private Window? _window;
    private Border? _bubble;
    private Polygon? _pointer;
    private TextBlock? _content;
    private TextBlock? _progress;
    private Rect _targetRect;
    private bool _dragging;
    private bool _manuallyPositioned;
    private string? _activeStepId;

    public async Task ShowAsync(
        AutomationElement target,
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken)
    {
        var physicalRect = target.Current.BoundingRectangle;
        if (physicalRect.IsEmpty || physicalRect.Width <= 0 || physicalRect.Height <= 0)
            throw new InvalidOperationException($"Windows target '{step.Id}' has no visible bounds.");

        var scale = GetTargetScale(target);
        var rect = new Rect(
            physicalRect.Left / scale,
            physicalRect.Top / scale,
            physicalRect.Width / scale,
            physicalRect.Height / scale);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            EnsureWindow();

            if (!string.Equals(_activeStepId, step.Id, StringComparison.Ordinal))
            {
                _activeStepId = step.Id;
                _manuallyPositioned = false;
                _pointer!.Visibility = Visibility.Visible;
            }

            _targetRect = rect;
            _content!.Text = step.Bubble.Content;
            _progress!.Text = $"שלב {stepNumber} מתוך {totalSteps}";
            AutomationProperties.SetName(_window!, step.Bubble.Content);

            _window!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _window.SizeToContent = SizeToContent.WidthAndHeight;
            _window.UpdateLayout();

            var bubbleWidth = Math.Max(_bubble!.ActualWidth, 260);
            var bubbleHeight = Math.Max(_bubble.ActualHeight, 90);
            var windowWidth = bubbleWidth + PointerSpace * 2;
            var windowHeight = bubbleHeight + PointerSpace * 2;

            if (!_manuallyPositioned)
            {
                var placement = ChoosePlacement(
                    rect,
                    step.Bubble.Placement,
                    windowWidth,
                    windowHeight,
                    SystemParameters.WorkArea);

                _window.Left = placement.X;
                _window.Top = placement.Y;
            }

            if (!_window.IsVisible)
                _window.Show();

            _window.UpdateLayout();

            if (_manuallyPositioned)
                _pointer!.Visibility = Visibility.Collapsed;
            else
            {
                _pointer!.Visibility = Visibility.Visible;
                UpdatePointer();
            }
        });

        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task HideAsync()
    {
        if (Application.Current is null)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_window?.IsVisible == true)
                _window.Hide();
        });
    }

    private static Point ChoosePlacement(
        Rect target,
        BubblePlacement preferred,
        double width,
        double height,
        Rect work)
    {
        // Respect the Guide placement as the authoritative side. The bubble may
        // cover unrelated UI; users can drag it away when desired. Only clamp
        // enough to keep the bubble on-screen instead of silently switching sides.
        var candidate = CandidateFor(preferred, target, width, height);
        return new Point(
            Math.Clamp(candidate.X, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4)),
            Math.Clamp(candidate.Y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4)));
    }

    private static Point CandidateFor(
        BubblePlacement side,
        Rect target,
        double width,
        double height) =>
        side switch
        {
            BubblePlacement.Left => new Point(
                target.Left - width - TargetGap,
                target.Top + (target.Height - height) / 2),
            BubblePlacement.Right => new Point(
                target.Right + TargetGap,
                target.Top + (target.Height - height) / 2),
            BubblePlacement.Top => new Point(
                target.Left + (target.Width - width) / 2,
                target.Top - height - TargetGap),
            _ => new Point(
                target.Left + (target.Width - width) / 2,
                target.Bottom + TargetGap)
        };

    private void UpdatePointer()
    {
        if (_window is null || _pointer is null || _bubble is null || _targetRect.IsEmpty)
            return;

        var bubbleRect = new Rect(
            _window.Left + PointerSpace,
            _window.Top + PointerSpace,
            _bubble.ActualWidth,
            _bubble.ActualHeight);

        var targetCenter = new Point(
            _targetRect.Left + _targetRect.Width / 2,
            _targetRect.Top + _targetRect.Height / 2);
        var bubbleCenter = new Point(
            bubbleRect.Left + bubbleRect.Width / 2,
            bubbleRect.Top + bubbleRect.Height / 2);

        var dx = targetCenter.X - bubbleCenter.X;
        var dy = targetCenter.Y - bubbleCenter.Y;

        const double size = 10d;
        Point p1;
        Point p2;
        Point tip;

        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            var y = PointerSpace + (_bubble.ActualHeight / 2);

            if (dx >= 0)
            {
                var x = PointerSpace + _bubble.ActualWidth;
                p1 = new Point(x, y - size);
                p2 = new Point(x, y + size);
                tip = new Point(x + PointerSpace, y);
            }
            else
            {
                var x = PointerSpace;
                p1 = new Point(x, y - size);
                p2 = new Point(x, y + size);
                tip = new Point(0, y);
            }
        }
        else
        {
            var x = PointerSpace + (_bubble.ActualWidth / 2);

            if (dy >= 0)
            {
                var y = PointerSpace + _bubble.ActualHeight;
                p1 = new Point(x - size, y);
                p2 = new Point(x + size, y);
                tip = new Point(x, y + PointerSpace);
            }
            else
            {
                var y = PointerSpace;
                p1 = new Point(x - size, y);
                p2 = new Point(x + size, y);
                tip = new Point(x, 0);
            }
        }

        _pointer.Points = new PointCollection { p1, p2, tip };
    }

    private static double GetTargetScale(AutomationElement target)
    {
        var walker = TreeWalker.ControlViewWalker;
        for (AutomationElement? current = target; current is not null; current = walker.GetParent(current))
        {
            var handle = new IntPtr(current.Current.NativeWindowHandle);
            if (handle == IntPtr.Zero)
                continue;

            var dpi = GetDpiForWindow(handle);
            if (dpi > 0)
                return dpi / 96d;
        }

        return 1d;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void EnsureWindow()
    {
        if (_window is not null)
            return;

        _content = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Foreground = Brushes.White,
            MaxWidth = 360,
            FlowDirection = FlowDirection.RightToLeft
        };

        _progress = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 236)),
            FlowDirection = FlowDirection.RightToLeft
        };

        var dragHandle = new TextBlock
        {
            Text = "⠿",
            FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 236)),
            Cursor = Cursors.SizeAll,
            ToolTip = "גרור להזזת הבועה",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var stack = new StackPanel();
        stack.Children.Add(dragHandle);
        stack.Children.Add(_content);
        stack.Children.Add(_progress);

        _bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(39, 67, 91)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(27, 48, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(PointerSpace),
            Child = stack
        };

        _pointer = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(39, 67, 91)),
            Stroke = new SolidColorBrush(Color.FromRgb(27, 48, 66)),
            StrokeThickness = 1,
            IsHitTestVisible = false
        };

        var canvas = new Canvas();
        canvas.Children.Add(_pointer);

        var root = new Grid
        {
            Background = Brushes.Transparent
        };
        root.Children.Add(_bubble);
        root.Children.Add(canvas);

        _window = new Window
        {
            Content = root,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight
        };

        dragHandle.MouseLeftButtonDown += (_, e) =>
        {
            if (_window is null || e.LeftButton != MouseButtonState.Pressed)
                return;

            _dragging = true;
            dragHandle.Cursor = Cursors.Hand;
            try
            {
                _window.DragMove();
            }
            finally
            {
                _dragging = false;
                _manuallyPositioned = true;
                dragHandle.Cursor = Cursors.SizeAll;
                _pointer!.Visibility = Visibility.Collapsed;
            }
        };

        _window.LocationChanged += (_, _) =>
        {
            if (!_manuallyPositioned && (_dragging || _window.IsVisible))
                UpdatePointer();
        };

        AutomationProperties.SetAutomationId(_window, "DapLearnerBubble");
    }
}
