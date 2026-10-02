using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DAP.Core.Guides;

namespace DAP.Runtime.Windows.Bubbles;

public sealed class WindowsBubblePresenter
{
    private Window? _window;
    private TextBlock? _content;
    private TextBlock? _progress;

    public async Task ShowAsync(
        AutomationElement target,
        GuideStep step,
        int stepNumber,
        int totalSteps,
        CancellationToken cancellationToken)
    {
        var rect = target.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException($"Windows target '{step.Id}' has no visible bounds.");

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            EnsureWindow();

            _content!.Text = step.Bubble.Content;
            _progress!.Text = $"שלב {stepNumber} מתוך {totalSteps}";
            AutomationProperties.SetName(_window!, step.Bubble.Content);

            _window!.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _window.SizeToContent = SizeToContent.WidthAndHeight;
            _window.UpdateLayout();

            var width = Math.Max(_window.ActualWidth, 260);
            var height = Math.Max(_window.ActualHeight, 90);
            var gap = 10d;

            var left = step.Bubble.Placement switch
            {
                BubblePlacement.Left => rect.Left - width - gap,
                BubblePlacement.Right => rect.Right + gap,
                _ => rect.Left + Math.Max(0, (rect.Width - width) / 2)
            };

            var top = step.Bubble.Placement switch
            {
                BubblePlacement.Top => rect.Top - height - gap,
                BubblePlacement.Bottom => rect.Bottom + gap,
                _ => rect.Top + Math.Max(0, (rect.Height - height) / 2)
            };

            var work = SystemParameters.WorkArea;
            _window.Left = Math.Clamp(left, work.Left + 4, Math.Max(work.Left + 4, work.Right - width - 4));
            _window.Top = Math.Clamp(top, work.Top + 4, Math.Max(work.Top + 4, work.Bottom - height - 4));

            if (!_window.IsVisible)
                _window.Show();
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

        var stack = new StackPanel();
        stack.Children.Add(_content);
        stack.Children.Add(_progress);

        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(39, 67, 91)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(27, 48, 66)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = stack
        };

        _window = new Window
        {
            Content = border,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            ShowActivated = false,
            SizeToContent = SizeToContent.WidthAndHeight
        };
        AutomationProperties.SetAutomationId(_window, "DapLearnerBubble");
    }
}
