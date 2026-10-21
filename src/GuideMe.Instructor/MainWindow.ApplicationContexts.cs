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
        LoadExistingGuide_Click(sender, e);
        if (_loadedGuide?.Id != guide.Id) return;
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
                    if (!matcher.TryGetProperty("Kind", out var property) ||
                        !matcher.TryGetProperty("Value", out var value)) continue;
                    var kind = property.GetString();
                    var expected = value.GetString();
                    var runtime = row["Runtime"].ToString();
                    bool match = false;
                    if (runtime == "Web" && kind == "UrlHost" &&
                        Uri.TryCreate(_capture.ActiveWebUrl, UriKind.Absolute, out var uri))
                        match = string.Equals(uri.Authority, expected, StringComparison.OrdinalIgnoreCase);
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
    private void CompareApplicationContexts_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedGuide is null)
        {
            MessageBox.Show("יש לטעון לומדה לפני ההשוואה.", "GuideMe Instructor");
            return;
        }
        try
        {
            var guides = V5GuideReader.ListGuides();
            var originalKey = SelectedRuntime.Text == "Windows"
                ? "sampleapp-windows-guide" : "sampleapp-web-guide";
            var original = System.Linq.Enumerable.FirstOrDefault(guides,
                g => string.Equals(g.Key, originalKey, StringComparison.Ordinal));
            if (original is null)
            {
                MessageBox.Show("לומדת הדוגמה המקורית אינה נמצאת במסד הנתונים הפעיל.", "GuideMe Instructor");
                return;
            }
            var current = V5GuideReader.ReadTable(_loadedGuide.Id, "ApplicationContexts");
            var reference = V5GuideReader.ReadTable(original.Id, "ApplicationContexts");
            var comparison = "לומדה נוכחית (" + _loadedGuide.Id + "):\n" + current +
                "\nלומדת דוגמה מקורית (" + original.Id + "):\n" + reference +
                "\nיש להשוות את Runtime, ContextId ו-MatchersJson. " +
                "זיהוי UrlHost=localhost בלבד אינו מבדיל בין יישומים מקומיים שונים.";
            var dialog = new Window
            {
                Title = "השוואת הקשרי אפליקציה",
                Owner = this,
                Width = 760,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new TextBox
                {
                    Text = comparison,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(12)
                }
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show("השוואת ההקשרים נכשלה: " + ex.Message, "GuideMe Instructor");
        }
    }

    private void DeleteGuide_Click(object sender, RoutedEventArgs e)
    {
        if (ExistingGuides.SelectedItem is not V5GuideReader.GuideInfo guide)
        {
            MessageBox.Show("בחר לומדה למחיקה.", "GuideMe Instructor");
            return;
        }
        if (_state != RecordingState.Ready)
        {
            MessageBox.Show("יש לסיים הקלטה או סקירת שלב לפני מחיקת לומדה.", "GuideMe Instructor");
            return;
        }
        if (!ConfirmGuideDeletion(guide)) return;
        try
        {
            V5GuideWriter.DeleteGuide(guide.Id);
            ClearWorkspace_Click(sender, e);
            ExistingGuides.ItemsSource = V5GuideReader.ListGuides();
            DatabaseStatus.Text = "הלומדה וכל מרכיביה נמחקו ממסד הנתונים.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("מחיקת הלומדה נכשלה: " + ex.Message,
                "GuideMe Instructor", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

}
