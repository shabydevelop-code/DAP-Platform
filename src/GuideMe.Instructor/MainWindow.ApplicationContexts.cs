using System;
using System.Windows;

namespace GuideMe.Instructor;

public partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private async void SelectApplicationWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedGuide is null) return;
        TargetSelectionStatus.Text = "עבור לחלון היעד. הזיהוי יתבצע בעוד שלוש שניות.";
        await System.Threading.Tasks.Task.Delay(3000);
        try
        {
            var window = GetForegroundWindow();
            GetWindowThreadProcessId(window, out var pid);
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            if (name.Equals("GuideMe.Instructor", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("יש לבחור חלון יעד שאינו Instructor.");
            if (name is "chrome" or "msedge" or "firefox")
            {
                if (!Uri.TryCreate(_capture.ActiveWebUrl, UriKind.Absolute, out var uri))
                    throw new InvalidOperationException("לא התקבלה כתובת אתר מהתוסף.");
                SelectedRuntime.Text = "Web";
                SelectedMatcherProperty.Text = "UrlHost";
                SelectedMatcherValue.Text = uri.Host;
                if (string.IsNullOrWhiteSpace(SelectedContextId.Text)) SelectedContextId.Text = "web-main";
            }
            else
            {
                SelectedRuntime.Text = "Windows";
                SelectedMatcherProperty.Text = "ProcessName";
                SelectedMatcherValue.Text = name;
                if (string.IsNullOrWhiteSpace(SelectedContextId.Text)) SelectedContextId.Text = "windows-main";
            }
            TargetSelectionStatus.Text = "החלון זוהה. בדוק ושמור את ההקשר.";
        }
        catch (Exception ex)
        {
            TargetSelectionStatus.Text = "בחירת חלון נכשלה: " + ex.Message;
        }
    }

    private void SaveApplicationContext_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedGuide is null) return;
        try
        {
            V5GuideWriter.SaveApplicationContext(_loadedGuide.Id, SelectedContextId.Text,
                SelectedRuntime.Text, SelectedMatcherProperty.Text, SelectedMatcherValue.Text);
            ContextsDetails.Text = V5GuideReader.ReadTable(_loadedGuide.Id, "ApplicationContexts");
            TargetSelectionStatus.Text = "הקשר האפליקציה נשמר.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "GuideMe Instructor");
        }
    }
}
