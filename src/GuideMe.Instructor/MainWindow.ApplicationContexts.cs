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
        SelectedRuntime.Clear();
        SelectedContextId.Clear();
        SelectedMatcherProperty.Clear();
        SelectedMatcherValue.Clear();
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private void SelectApplicationWindow_Click(object sender, RoutedEventArgs e)
    {
        var picker = new TargetWindowPicker(_capture);
        TargetSelectionStatus.Text = "מצב בחירת חלון פעיל. ניתן לעבור בין אפליקציות ללא הגבלת זמן.";
        var selected = picker.ShowDialog() == true ? picker.Selection : null;
        if (selected is null)
        {
            TargetSelectionStatus.Text = "בחירת החלון בוטלה.";
            return;
        }
        SelectedRuntime.Text = selected.Runtime;
        SelectedMatcherProperty.Text = selected.Property;
        SelectedMatcherValue.Text = selected.Value;
        if (string.IsNullOrWhiteSpace(SelectedContextId.Text) ||
            (selected.Runtime == "Windows" && SelectedContextId.Text == "web-main") ||
            (selected.Runtime == "Web" && SelectedContextId.Text == "windows-main"))
            SelectedContextId.Text = selected.Runtime == "Web" ? "web-main" : "windows-main";
        TargetSelectionStatus.Text = "נבחר חלון יעד: " + selected.Title;
        NewGuideTargetStatus.Text = "נבחר: " + selected.Title;
        _pendingNewGuideTarget = NewGuidePanel.Visibility == Visibility.Visible;
        if (_pendingNewGuideTarget) CreateNewGuideButton.IsEnabled = true;
    }

    private void ExistingGuideSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExistingGuides.SelectedItem is not V5GuideReader.GuideInfo guide) return;
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
