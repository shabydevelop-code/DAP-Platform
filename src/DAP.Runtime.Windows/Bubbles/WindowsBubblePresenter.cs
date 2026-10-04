using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using DAP.Core.Guides;
using DAP.Core.Localization;

namespace DAP.Runtime.Windows.Bubbles;

public sealed class WindowsBubblePresenter
{
    private const double PointerSpace = 16d;
    private const double TargetGap = 10d;

    private readonly IUiTextProvider _texts;
    private Window? _window;
    private Window? _highlightWindow;
    private Border? _highlightBorder;
    private Border? _bubble;
    private Polygon? _pointer;
    private TextBlock? _content;
    private TextBlock? _progress;
    private Rect _targetRect;
    private bool _dragging;
    private bool _manuallyPositioned;
    private string? _activeStepId;
    private BubblePlacement _activePlacement = BubblePlacement.Bottom;

    public WindowsBubblePresenter(IUiTextProvider texts)
    {
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
    }

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

            var previousTargetRect = _targetRect;
            _targetRect = rect;
            UpdateTargetHighlight(rect);
            _content!.Text = step.Bubble.Content;
            _progress!.Text = _texts.Format("Learner.StepProgress", stepNumber, totalSteps);
            AutomationProperties.SetName(_window!, step.Bubble.Content);

            _window!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _window.SizeToContent = SizeToContent.WidthAndHeight;
            _window.UpdateLayout();

            var bubbleWidth = _bubble!.ActualWidth;
            var bubbleHeight = _bubble.ActualHeight;
            var windowWidth = Math.Max(_window.ActualWidth, bubbleWidth + PointerSpace * 2);
            var windowHeight = Math.Max(_window.ActualHeight, bubbleHeight + PointerSpace * 2);

            if (!_manuallyPositioned)
            {
                var placement = ChoosePlacement(
                    rect,
                    step.Bubble.Placement,
                    windowWidth,
                    windowHeight,
                    SystemParameters.WorkArea);

                _activePlacement = placement.Side;
                _window.Left = placement.Position.X;
                _window.Top = placement.Position.Y;
            }
            else if (!previousTargetRect.IsEmpty)
            {
                // A learner-dragged bubble keeps its manual offset from the target
                // while the application window moves or resizes.
                var deltaX = rect.Left + rect.Width / 2
                             - (previousTargetRect.Left + previousTargetRect.Width / 2);
                var deltaY = rect.Top + rect.Height / 2
                             - (previousTargetRect.Top + previousTargetRect.Height / 2);
                var work = SystemParameters.WorkArea;
                _window.Left = Math.Clamp(
                    _window.Left + deltaX,
                    work.Left + 4,
                    Math.Max(work.Left + 4, work.Right - windowWidth - 4));
                _window.Top = Math.Clamp(
                    _window.Top + deltaY,
                    work.Top + 4,
                    Math.Max(work.Top + 4, work.Bottom - windowHeight - 4));
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

    public Task WaitForGuideCompletedDismissalAsync(
        CancellationToken cancellationToken = default)
        => WaitForCenteredBubbleDismissalAsync(
            "DapLearnerCompletionBubble",
            "DapLearnerCompletionFinish",
            _texts.Get("Learner.GuideCompleted"),
            _texts.Get("Learner.Finish"),
            progressText: null,
            cancellationToken);

    public Task WaitForCenteredStepDismissalAsync(
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken = default)
        => WaitForCenteredBubbleDismissalAsync(
            "DapLearnerCenteredBubble",
            "DapLearnerCenteredConfirm",
            step.Bubble.Content,
            _texts.Get("Learner.Confirm"),
            _texts.Format("Learner.StepProgress", stepNumber, totalSteps),
            cancellationToken);

    private async Task WaitForCenteredBubbleDismissalAsync(
        string windowAutomationId,
        string actionAutomationId,
        string content,
        string actionText,
        string? progressText,
        CancellationToken cancellationToken)
    {
        if (Application.Current is null)
            return;

        var dismissed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Window? centeredWindow = null;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_window?.IsVisible == true)
                _window.Hide();
            if (_highlightWindow?.IsVisible == true)
                _highlightWindow.Hide();

            var message = new TextBlock
            {
                Text = content,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Foreground = Brushes.White,
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FlowDirection = _texts.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
            };

            var actionButton = new Button
            {
                Content = actionText,
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(18, 6, 18, 6),
                HorizontalAlignment = HorizontalAlignment.Center,
                MinWidth = 90
            };
            AutomationProperties.SetAutomationId(actionButton, actionAutomationId);

            var dragHandle = new TextBlock
            {
                Text = "⠿",
                FontSize = 17,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 236)),
                Cursor = Cursors.SizeAll,
                ToolTip = _texts.Get("Learner.DragBubble"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var stack = new StackPanel();
            stack.Children.Add(dragHandle);
            stack.Children.Add(message);

            if (!string.IsNullOrWhiteSpace(progressText))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = progressText,
                    Margin = new Thickness(0, 8, 0, 0),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 236)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FlowDirection = _texts.IsRightToLeft
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight
                });
            }

            stack.Children.Add(actionButton);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(39, 67, 91)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(27, 48, 66)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14),
                Child = stack
            };

            centeredWindow = new Window
            {
                Content = border,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowInTaskbar = false,
                Topmost = true,
                ShowActivated = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            AutomationProperties.SetAutomationId(centeredWindow, windowAutomationId);
            AutomationProperties.SetName(centeredWindow, content);

            actionButton.Click += (_, _) =>
            {
                if (centeredWindow.IsVisible)
                    centeredWindow.Close();
                dismissed.TrySetResult();
            };

            dragHandle.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed)
                    return;

                try
                {
                    centeredWindow.DragMove();
                }
                catch (InvalidOperationException)
                {
                }
            };

            centeredWindow.Closed += (_, _) => dismissed.TrySetResult();
            centeredWindow.Show();
        });

        using var registration = cancellationToken.Register(() =>
        {
            dismissed.TrySetCanceled(cancellationToken);
            if (centeredWindow is not null && Application.Current is not null)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (centeredWindow.IsVisible)
                        centeredWindow.Close();
                });
            }
        });

        await dismissed.Task;
    }

    public async Task HideAsync()
    {
        if (Application.Current is null)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_window?.IsVisible == true)
                _window.Hide();
            if (_highlightWindow?.IsVisible == true)
                _highlightWindow.Hide();
        });
    }

    private readonly record struct PlacementResult(Point Position, BubblePlacement Side);

    private static PlacementResult ChoosePlacement(
        Rect target,
        BubblePlacement preferred,
        double width,
        double height,
        Rect work)
    {
        var opposite = preferred switch
        {
            BubblePlacement.Left => BubblePlacement.Right,
            BubblePlacement.Right => BubblePlacement.Left,
            BubblePlacement.Top => BubblePlacement.Bottom,
            _ => BubblePlacement.Top
        };

        var perpendicular = preferred is BubblePlacement.Top or BubblePlacement.Bottom
            ? new[] { BubblePlacement.Right, BubblePlacement.Left }
            : new[] { BubblePlacement.Bottom, BubblePlacement.Top };

        foreach (var side in new[] { preferred, opposite }.Concat(perpendicular))
        {
            var candidate = ClampSecondaryAxis(
                CandidateFor(side, target, width, height),
                side,
                width,
                height,
                work);

            if (Fits(candidate, width, height, work)
                && !new Rect(candidate.X, candidate.Y, width, height).IntersectsWith(target))
                return new PlacementResult(candidate, side);
        }

        // Extremely small work areas can make every side impossible. Preserve
        // visibility as a last resort, but prefer the side with the most room.
        var fallbackSide = AvailableSpace(preferred, target, work) >= AvailableSpace(opposite, target, work)
            ? preferred
            : opposite;
        var fallback = CandidateFor(fallbackSide, target, width, height);
        return new PlacementResult(
            new Point(
                Math.Clamp(fallback.X, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4)),
                Math.Clamp(fallback.Y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4))),
            fallbackSide);
    }

    private static Point ClampSecondaryAxis(
        Point candidate,
        BubblePlacement side,
        double width,
        double height,
        Rect work) =>
        side is BubblePlacement.Top or BubblePlacement.Bottom
            ? new Point(
                Math.Clamp(candidate.X, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4)),
                candidate.Y)
            : new Point(
                candidate.X,
                Math.Clamp(candidate.Y, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4)));

    private static bool Fits(Point candidate, double width, double height, Rect work) =>
        candidate.X >= work.Left + 4
        && candidate.Y >= work.Top + 4
        && candidate.X + width <= work.Right - 4
        && candidate.Y + height <= work.Bottom - 4;

    private static double AvailableSpace(BubblePlacement side, Rect target, Rect work) =>
        side switch
        {
            BubblePlacement.Left => target.Left - work.Left,
            BubblePlacement.Right => work.Right - target.Right,
            BubblePlacement.Top => target.Top - work.Top,
            _ => work.Bottom - target.Bottom
        };

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

        var targetCenter = new Point(
            _targetRect.Left + _targetRect.Width / 2,
            _targetRect.Top + _targetRect.Height / 2);

        const double size = 10d;
        Point p1;
        Point p2;
        Point tip;

        switch (_activePlacement)
        {
            case BubblePlacement.Left:
            {
                var x = PointerSpace + _bubble.ActualWidth;
                var targetY = targetCenter.Y - _window.Top;
                var y = Math.Clamp(
                    targetY,
                    PointerSpace + size,
                    PointerSpace + _bubble.ActualHeight - size);
                p1 = new Point(x, y - size);
                p2 = new Point(x, y + size);
                tip = new Point(x + PointerSpace, y);
                break;
            }
            case BubblePlacement.Right:
            {
                var x = PointerSpace;
                var targetY = targetCenter.Y - _window.Top;
                var y = Math.Clamp(
                    targetY,
                    PointerSpace + size,
                    PointerSpace + _bubble.ActualHeight - size);
                p1 = new Point(x, y - size);
                p2 = new Point(x, y + size);
                tip = new Point(0, y);
                break;
            }
            case BubblePlacement.Top:
            {
                var y = PointerSpace + _bubble.ActualHeight;
                var targetX = targetCenter.X - _window.Left;
                var x = Math.Clamp(
                    targetX,
                    PointerSpace + size,
                    PointerSpace + _bubble.ActualWidth - size);
                p1 = new Point(x - size, y);
                p2 = new Point(x + size, y);
                tip = new Point(x, y + PointerSpace);
                break;
            }
            default:
            {
                var y = PointerSpace;
                var targetX = targetCenter.X - _window.Left;
                var x = Math.Clamp(
                    targetX,
                    PointerSpace + size,
                    PointerSpace + _bubble.ActualWidth - size);
                p1 = new Point(x - size, y);
                p2 = new Point(x + size, y);
                tip = new Point(x, 0);
                break;
            }
        }

        _pointer.Points = new PointCollection { p1, p2, tip };
    }

    private void UpdateTargetHighlight(Rect target)
    {
        EnsureHighlightWindow();

        const double inset = 4d;
        _highlightWindow!.Left = target.Left - inset;
        _highlightWindow.Top = target.Top - inset;
        _highlightWindow.Width = Math.Max(1, target.Width + inset * 2);
        _highlightWindow.Height = Math.Max(1, target.Height + inset * 2);

        if (!_highlightWindow.IsVisible)
            _highlightWindow.Show();
    }

    private void EnsureHighlightWindow()
    {
        if (_highlightWindow is not null)
            return;

        _highlightBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(45, 156, 219)),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        _highlightWindow = new Window
        {
            Content = _highlightBorder,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            IsHitTestVisible = false
        };

        _highlightWindow.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_highlightWindow).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            style |= WsExTransparent | WsExNoActivate | WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        };

        AutomationProperties.SetAutomationId(_highlightWindow, "DapLearnerTargetHighlight");
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

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong);

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
            FlowDirection = _texts.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        var dragHandle = new TextBlock
        {
            Text = "⠿",
            FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(220, 228, 236)),
            Cursor = Cursors.SizeAll,
            ToolTip = _texts.Get("Learner.DragBubble"),
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
            _manuallyPositioned = true;
            _pointer!.Visibility = Visibility.Collapsed;
            dragHandle.Cursor = Cursors.Hand;
            try
            {
                _window.DragMove();
            }
            finally
            {
                _dragging = false;
                dragHandle.Cursor = Cursors.SizeAll;
                _pointer!.Visibility = Visibility.Collapsed;
            }
        };

        _window.LocationChanged += (_, _) =>
        {
            if (!_manuallyPositioned && (_dragging || _window.IsVisible))
                UpdatePointer();
        };

        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            style |= WsExNoActivate | WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        };

        AutomationProperties.SetAutomationId(_window, "DapLearnerBubble");
    }
}
