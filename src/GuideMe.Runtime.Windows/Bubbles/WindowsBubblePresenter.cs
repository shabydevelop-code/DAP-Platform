using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using GuideMe.Core.Guides;
using GuideMe.Core.Localization;

namespace GuideMe.Runtime.Windows.Bubbles;

public sealed class WindowsBubblePresenter
{
    private const double BubbleWindowInset = 16d;
    private const double TargetGap = 10d;

    private readonly IUiTextProvider _texts;
    private Window? _window;
    private Window? _highlightWindow;
    private Border? _highlightBorder;
    private Border? _bubble;
    private TextBlock? _content;
    private TextBlock? _progress;
    private Button? _manualConfirm;
    private string? _manualConfirmedStepId;
    private Border? _automaticBadge;
    private TextBlock? _automaticBadgeText;
    private Rect _targetRect;
    private bool _dragging;
    private bool _hiddenByViewport;
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
        CancellationToken cancellationToken,
        string? automaticStepLabel = null)
    {
        var physicalRect = GetPresentationBounds(target);
        if (physicalRect.IsEmpty || physicalRect.Width <= 0 || physicalRect.Height <= 0)
            throw new InvalidOperationException($"Windows target '{step.Id}' has no visible bounds.");

        var viewportTimer = Stopwatch.StartNew();
        var visibleInViewport = IsFullyVisibleInScrollableViewport(target, physicalRect);
        viewportTimer.Stop();
        if (!visibleInViewport)
        {
            if (!_hiddenByViewport)
                Console.Error.WriteLine($"[GuideMe Windows bubble viewport] Step '{step.Id}' hidden; viewportCheck={viewportTimer.ElapsedMilliseconds} ms.");
            _hiddenByViewport = true;
            await HideAsync();
            return;
        }
        var returningFromViewport = _hiddenByViewport;
        var presentationTimer = Stopwatch.StartNew();

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
            }

            _targetRect = rect;
            UpdateTargetHighlight(rect);
            SetFormattedBubble(_content!, step.Bubble.Content);
            _automaticBadgeText!.Text = automaticStepLabel ?? string.Empty;
            _automaticBadge!.Visibility = string.IsNullOrWhiteSpace(automaticStepLabel)
                ? Visibility.Collapsed
                : Visibility.Visible;
            _progress!.Text = _texts.Format("Learner.StepProgress", stepNumber, totalSteps);
            _manualConfirm!.Visibility = step.AdvanceMode == StepAdvanceMode.Manual
                ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(_window!, step.Bubble.Content);

            _window!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _window.SizeToContent = SizeToContent.WidthAndHeight;
            _window.UpdateLayout();

            var bubbleWidth = _bubble!.ActualWidth;
            var bubbleHeight = _bubble.ActualHeight;
            var windowWidth = Math.Max(_window.ActualWidth, bubbleWidth + BubbleWindowInset * 2);
            var windowHeight = Math.Max(_window.ActualHeight, bubbleHeight + BubbleWindowInset * 2);

            if (!_manuallyPositioned && !_dragging)
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

            if (!_window.IsVisible)
                _window.Show();

            _window.UpdateLayout();

            // No directional arrow; the target highlight remains visible.
        });

        if (returningFromViewport)
        {
            _hiddenByViewport = false;
            Console.Error.WriteLine($"[GuideMe Windows bubble viewport] Step '{step.Id}' restored; viewportCheck={viewportTimer.ElapsedMilliseconds} ms, presentation={presentationTimer.ElapsedMilliseconds} ms.");
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public bool IsManualStepConfirmed(string stepId) =>
        string.Equals(_manualConfirmedStepId, stepId, StringComparison.Ordinal);

    public void ResetManualConfirmation() => _manualConfirmedStepId = null;

    public static Rect GetPresentationBounds(AutomationElement target)
    {
        var bounds = target.Current.BoundingRectangle;
        if (target.Current.ControlType != ControlType.Edit ||
            !target.Current.AutomationId.StartsWith("UIA_AutomationId_Word_Content_Page_", StringComparison.Ordinal))
            return bounds;

        // Word's Edit bounds represent its text/caret, and are empty on a blank page.
        // Use the nearest page-sized UIA ancestor even before the first keystroke.
        var processId = target.Current.ProcessId;
        var parent = TreeWalker.RawViewWalker.GetParent(target);
        for (var depth = 0; parent is not null && depth < 8; depth++,
             parent = TreeWalker.RawViewWalker.GetParent(parent))
        {
            if (parent.Current.ProcessId != processId)
                break;
            var candidate = parent.Current.BoundingRectangle;
            if (!candidate.IsEmpty && candidate.Width >= 150 && candidate.Height >= 100 &&
                (bounds.IsEmpty || bounds.Width < 12 || candidate.IntersectsWith(bounds)))
                return candidate;
        }
        return bounds;
    }

    public Task WaitForGuideCompletedDismissalAsync(
        CancellationToken cancellationToken = default)
        => WaitForCenteredBubbleDismissalAsync(
            "GuideMeLearnerCompletionBubble",
            "GuideMeLearnerCompletionFinish",
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
            "GuideMeLearnerCenteredBubble",
            "GuideMeLearnerCenteredConfirm",
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
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 45, 61)),
                MaxWidth = 360,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FlowDirection = _texts.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
            };

            SetFormattedBubble(message, content);

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
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
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
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    FlowDirection = _texts.IsRightToLeft
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight
                });
            }

            stack.Children.Add(actionButton);

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(244, 197, 66)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(18),
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
            BorderBrush = new SolidColorBrush(Color.FromRgb(255, 223, 0)),
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

        AutomationProperties.SetAutomationId(_highlightWindow, "GuideMeLearnerTargetHighlight");
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


    private static void SetFormattedBubble(TextBlock block, string? content)
    {
        block.Inlines.Clear();
        var bold = 0;
        var italic = 0;
        var underline = 0;
        foreach (var token in Regex.Split(content ?? string.Empty, @"(<[^>]*>)"))
        {
            if (token.Length == 0) continue;
            var tag = Regex.Match(token, @"^<\s*(/?)\s*(strong|b|em|i|u|br)\s*/?\s*>$", RegexOptions.IgnoreCase);
            if (tag.Success)
            {
                var delta = tag.Groups[1].Value == "/" ? -1 : 1;
                switch (tag.Groups[2].Value.ToLowerInvariant())
                {
                    case "strong":
                    case "b": bold = Math.Max(0, bold + delta); break;
                    case "em":
                    case "i": italic = Math.Max(0, italic + delta); break;
                    case "u": underline = Math.Max(0, underline + delta); break;
                    case "br": block.Inlines.Add(new LineBreak()); break;
                }
                continue;
            }
            // Ignore unsupported markup rather than executing or displaying it.
            if (token.StartsWith("<", StringComparison.Ordinal) && token.EndsWith(">", StringComparison.Ordinal))
                continue;
            block.Inlines.Add(new Run(WebUtility.HtmlDecode(token))
            {
                FontWeight = bold > 0 ? FontWeights.Bold : FontWeights.SemiBold,
                FontStyle = italic > 0 ? FontStyles.Italic : FontStyles.Normal,
                TextDecorations = underline > 0 ? TextDecorations.Underline : null
            });
        }
    }

    private void EnsureWindow()
    {
        if (_window is not null)
            return;

        _content = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 45, 61)),
            MaxWidth = 360,
            FlowDirection = FlowDirection.RightToLeft
        };

        _automaticBadgeText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 45, 61)),
            FlowDirection = FlowDirection.RightToLeft
        };

        _automaticBadge = new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(8, 2, 8, 2),
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = _automaticBadgeText,
            Visibility = Visibility.Collapsed
        };

        _progress = new TextBlock
        {
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            FlowDirection = _texts.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };

        var dragHandle = new TextBlock
        {
            Text = "⠿",
            FontSize = 17,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Cursor = Cursors.Hand,
            ToolTip = _texts.Get("Learner.DragBubble"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var stack = new StackPanel();
        stack.Children.Add(dragHandle);
        stack.Children.Add(_automaticBadge);
        stack.Children.Add(_content);
        stack.Children.Add(_progress);
        _manualConfirm = new Button
        {
            Content = _texts.Get("Learner.Confirm"),
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(18, 6, 18, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetAutomationId(_manualConfirm, "GuideMeLearnerAttachedConfirm");
        _manualConfirm.Click += (_, _) => _manualConfirmedStepId = _activeStepId;
        stack.Children.Add(_manualConfirm);

        _bubble = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(244, 197, 66)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14),
            Margin = new Thickness(BubbleWindowInset),
            Child = stack
        };

        var root = new Grid
        {
            Background = Brushes.Transparent
        };
        root.Children.Add(_bubble);

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
            dragHandle.Cursor = Cursors.SizeAll;
            try
            {
                _window.DragMove();
            }
            finally
            {
                _dragging = false;
                dragHandle.Cursor = Cursors.Hand;
            }
        };

        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
            style |= WsExNoActivate | WsExToolWindow;
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        };

        AutomationProperties.SetAutomationId(_window, "GuideMeLearnerBubble");
    }

    private static bool IsFullyVisibleInScrollableViewport(AutomationElement target, Rect targetRect)
    {
        try
        {
            // Word reports its empty Edit/caret as offscreen until the first
            // character is entered, even while the containing page is visible.
            // For that provider, the visible page ancestor determines presentation.
            if (target.Current.IsOffscreen)
            {
                if (target.Current.ControlType != ControlType.Edit ||
                    !target.Current.AutomationId.StartsWith("UIA_AutomationId_Word_Content_Page_", StringComparison.Ordinal) ||
                    targetRect.IsEmpty)
                    return false;
                var pageVisible = false;
                for (var parent = TreeWalker.RawViewWalker.GetParent(target); parent is not null;
                     parent = TreeWalker.RawViewWalker.GetParent(parent))
                {
                    var bounds = parent.Current.BoundingRectangle;
                    if (!bounds.IsEmpty && bounds.Width >= 150 && bounds.Height >= 100 &&
                        !parent.Current.IsOffscreen)
                    {
                        pageVisible = true;
                        break;
                    }
                }
                if (!pageVisible)
                    return false;
            }

            var walker = TreeWalker.ControlViewWalker;
            for (var ancestor = walker.GetParent(target);
                 ancestor is not null;
                 ancestor = walker.GetParent(ancestor))
            {
                if (!ancestor.TryGetCurrentPattern(ScrollPattern.Pattern, out var rawPattern))
                    continue;

                var scroll = (ScrollPattern)rawPattern;
                if (!scroll.Current.VerticallyScrollable)
                    continue;

                var viewport = ancestor.Current.BoundingRectangle;
                if (viewport.IsEmpty)
                    continue;

                const double tolerance = 1d;
                if (targetRect.Left < viewport.Left - tolerance
                    || targetRect.Top < viewport.Top - tolerance
                    || targetRect.Right > viewport.Right + tolerance
                    || targetRect.Bottom > viewport.Bottom + tolerance)
                    return false;
            }

            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

}
