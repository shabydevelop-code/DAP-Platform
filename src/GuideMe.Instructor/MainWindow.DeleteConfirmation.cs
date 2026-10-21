using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GuideMe.Instructor;

public partial class MainWindow
{
    private bool ConfirmGuideDeletion(V5GuideReader.GuideInfo guide)
    {
        var dialog = new Window
        {
            Title = "אישור מחיקת לומדה",
            Owner = this,
            Width = 480,
            Height = 240,
            MinWidth = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            Background = Brushes.White,
            ShowInTaskbar = false
        };
        var layout = new StackPanel { Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock
        {
            Text = "מחיקת לומדה לצמיתות",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        layout.Children.Add(new TextBlock
        {
            Text = "לומדה מספר " + guide.Id + " — " + guide.Name +
                   "\nכל השלבים, הרכיבים, הכללים והקשרי האפליקציה שלה יימחקו לצמיתות. לא ניתן לבטל את הפעולה.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 18)
        });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var delete = new Button
        {
            Content = "כן, מחק לומדה",
            MinWidth = 125,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 10, 0)
        };
        var cancel = new Button { Content = "ביטול", MinWidth = 90, Padding = new Thickness(12, 8, 12, 8), IsCancel = true };
        delete.Click += (_, _) => dialog.DialogResult = true;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        actions.Children.Add(delete);
        actions.Children.Add(cancel);
        layout.Children.Add(actions);
        dialog.Content = layout;
        return dialog.ShowDialog() == true;
    }
}
