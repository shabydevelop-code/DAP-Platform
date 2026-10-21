using System;
using System.Windows;
using System.Windows.Controls;

namespace GuideMe.Instructor;

public partial class MainWindow
{
    private bool _pendingNewGuideTarget;

    private void NewGuidePanel_VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (NewGuidePanel?.Visibility != Visibility.Visible || CreateNewGuideButton is null) return;
        _pendingNewGuideTarget = false;
        CreateNewGuideButton.IsEnabled = false;
        NewGuideTargetStatus.Text = "טרם נבחר חלון יעד";
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private async void SelectApplicationWindow_Click(object sender, RoutedEventArgs e)
    {
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
            NewGuideTargetStatus.Text = $"נבחר: {SelectedRuntime.Text} — {SelectedMatcherValue.Text}";
            _pendingNewGuideTarget = NewGuidePanel.Visibility == Visibility.Visible;
            if (_pendingNewGuideTarget) CreateNewGuideButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            TargetSelectionStatus.Text = "בחירת חלון נכשלה: " + ex.Message;
        }
    }

    private void ExistingGuideSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExistingGuides.SelectedItem is not V5GuideReader.GuideInfo guide) return;
        if (_pendingNewGuideTarget && NewGuidePanel.Visibility == Visibility.Visible &&
            string.Equals(guide.Name, NewGuideName.Text.Trim(), StringComparison.Ordinal))
        {
            try
            {
                V5GuideWriter.SaveApplicationContext(guide.Id, SelectedContextId.Text,
                    SelectedRuntime.Text, SelectedMatcherProperty.Text, SelectedMatcherValue.Text);
                _pendingNewGuideTarget = false;
                TargetSelectionStatus.Text = "הקשר האפליקציה נשמר בלומדה החדשה.";
            }
            catch (Exception ex)
            {
                TargetSelectionStatus.Text = "שמירת ההקשר נכשלה: " + ex.Message;
            }
        }
        try
        {
            var contexts = V5GuideReader.ReadGuideTable(guide.Id, "ApplicationContexts");
            if (contexts.Rows.Count == 0)
            {
                TargetSelectionStatus.Text = "לא הוגדר הקשר אפליקציה. יש לבחור חלון.";
                return;
            }
            foreach (System.Data.DataRow row in contexts.Rows)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(row["MatchersJson"].ToString() ?? "[]");
                foreach (var matcher in doc.RootElement.EnumerateArray())
                {
                    if (!matcher.TryGetProperty("Property", out var property) ||
                        !matcher.TryGetProperty("Value", out var value)) continue;
                    var kind = property.GetString();
                    var expected = value.GetString();
                    var runtime = row["Runtime"].ToString();
                    bool match = false;
                    if (runtime == "Web" && kind == "UrlHost" &&
                        Uri.TryCreate(_capture.ActiveWebUrl, UriKind.Absolute, out var uri))
                        match = string.Equals(uri.Host, expected, StringComparison.OrdinalIgnoreCase);
                    if (runtime == "Windows" && kind == "ProcessName" && expected is not null)
                        match = System.Diagnostics.Process.GetProcessesByName(expected).Length > 0;
                    if (!match) continue;
                    SelectedRuntime.Text = runtime;
                    SelectedContextId.Text = row["ContextId"].ToString();
                    SelectedMatcherProperty.Text = kind;
                    SelectedMatcherValue.Text = expected;
                    TargetSelectionStatus.Text = "נמצא חלון תואם להקשר השמור.";
                    return;
                }
            }
            TargetSelectionStatus.Text = "לא נמצא חלון תואם. פתח את האפליקציה ובחר את החלון.";
        }
        catch (Exception ex)
        {
            TargetSelectionStatus.Text = "איתור הקשר נכשל: " + ex.Message;
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
