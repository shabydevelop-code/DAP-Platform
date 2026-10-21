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
        TargetSelectionStatus.Text = "בחירת אפליקציית יעד. ניתן לעבור בין אפליקציות ללא הגבלת זמן.";
        var selected = picker.ShowDialog() == true ? picker.Selection : null;
        if (selected is null)
        {
            TargetSelectionStatus.Text = "בחירת האפליקציה בוטלה.";
            return;
        }
        // A guide may contain Web and Windows contexts. Never reuse the current
        // context ID for a different application or runtime.
        var creating = NewGuidePanel.Visibility == Visibility.Visible;
        var contextId = selected.Runtime == "Web" ? "web-main" : "windows-main";
        if (!creating && _loadedGuide is not null)
        {
            var existing = V5GuideReader.ReadGuideTable(_loadedGuide.Id, "ApplicationContexts");
            var usedIds = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Data.DataRow row in existing.Rows)
            {
                var id = row["ContextId"].ToString() ?? "";
                usedIds.Add(id);
                if (!string.Equals(row["Runtime"].ToString(), selected.Runtime, StringComparison.Ordinal))
                    continue;
                using var matchers = System.Text.Json.JsonDocument.Parse(row["MatchersJson"].ToString() ?? "[]");
                foreach (var matcher in matchers.RootElement.EnumerateArray())
                {
                    if (!matcher.TryGetProperty("Value", out var value) ||
                        !string.Equals(value.GetString(), selected.Value, StringComparison.OrdinalIgnoreCase))
                        continue;
                    contextId = id;
                    goto ContextResolved;
                }
            }
            // A blank legacy matcher can be repaired when the user explicitly
            // selects a target of the same runtime.
            foreach (System.Data.DataRow row in existing.Rows)
            {
                if (!string.Equals(row["Runtime"].ToString(), selected.Runtime, StringComparison.Ordinal))
                    continue;
                using var matchers = System.Text.Json.JsonDocument.Parse(row["MatchersJson"].ToString() ?? "[]");
                foreach (var matcher in matchers.RootElement.EnumerateArray())
                {
                    if (matcher.TryGetProperty("Value", out var value) &&
                        string.IsNullOrWhiteSpace(value.GetString()))
                    {
                        contextId = row["ContextId"].ToString() ?? contextId;
                        goto ContextResolved;
                    }
                }
            }
            if (usedIds.Contains(contextId))
            {
                var baseId = contextId;
                var suffix = 2;
                while (usedIds.Contains(baseId + "-" + suffix)) suffix++;
                contextId = baseId + "-" + suffix;
            }
        }
    ContextResolved:
        SelectedRuntime.Text = selected.Runtime;
        SelectedContextId.Text = contextId;
        SelectedMatcherProperty.Text = selected.Property;
        SelectedMatcherValue.Text = selected.Value;
        TargetSelectionStatus.Text = "נבחרה אפליקציית יעד: " + selected.Title;
        NewGuideTargetStatus.Text = "נבחר: " + selected.Title;
        _pendingNewGuideTarget = NewGuidePanel.Visibility == Visibility.Visible;
        if (_pendingNewGuideTarget) CreateNewGuideButton.IsEnabled = true;
        if (!_pendingNewGuideTarget && _loadedGuide is not null)
        {
            try
            {
                V5GuideWriter.SaveApplicationContext(_loadedGuide.Id, SelectedContextId.Text,
                    selected.Runtime, selected.Property, selected.Value);
                ContextsDetails.Text = V5GuideReader.ReadTable(_loadedGuide.Id, "ApplicationContexts");
                TargetSelectionStatus.Text = "היעד זוהה והקשר האפליקציה נשמר אוטומטית.";
                RefreshConnection();
            }
            catch (Exception ex)
            {
                TargetSelectionStatus.Text = "שמירת ההקשר נכשלה: " + ex.Message;
                MessageBox.Show(TargetSelectionStatus.Text, "GuideMe Instructor",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ExistingGuideSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExistingGuides.SelectedItem is not V5GuideReader.GuideInfo guide) return;
        LoadExistingGuide_Click(sender, e);
        if (_loadedGuide?.Id != guide.Id) return;
        // Persisted identity is independent of whether its target is currently open.
        SelectedRuntime.Text = "";
        SelectedContextId.Text = "";
        SelectedMatcherProperty.Text = "";
        SelectedMatcherValue.Text = "";
        try
        {
            var contexts = V5GuideReader.ReadGuideTable(guide.Id, "ApplicationContexts");
            if (contexts.Rows.Count == 0)
            {
                TargetSelectionStatus.Text = "לא הוגדר הקשר אפליקציה. יש לבחור חלון.";
                RefreshConnection();
                return;
            }
            foreach (System.Data.DataRow row in contexts.Rows)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(row["MatchersJson"].ToString() ?? "[]");
                foreach (var matcher in doc.RootElement.EnumerateArray())
                {
                    var propertyName = matcher.TryGetProperty("Kind", out var kindProperty) ? "Kind" :
                        matcher.TryGetProperty("Property", out _) ? "Property" : null;
                    if (propertyName is null ||
                        !matcher.TryGetProperty("Value", out var value)) continue;
                    var kind = matcher.GetProperty(propertyName).GetString();
                    var expected = value.GetString();
                    var runtime = row["Runtime"].ToString();
                    if (string.IsNullOrWhiteSpace(expected) ||
                        (runtime == "Web" && kind != "UrlHost") ||
                        (runtime == "Windows" && kind != "ProcessName")) continue;
                    SelectedRuntime.Text = runtime;
                    SelectedContextId.Text = row["ContextId"].ToString();
                    SelectedMatcherProperty.Text = kind;
                    SelectedMatcherValue.Text = expected;
                    TargetSelectionStatus.Text = "הקשר האפליקציה נטען. הזמינות נבדקת באופן שוטף.";
                    RefreshConnection();
                    return;
                }
            }
            TargetSelectionStatus.Text = "לא נמצא הקשר נתמך בלומדה. בחר חלון יעד.";
            RefreshConnection();
        }
        catch (Exception ex)
        {
            TargetSelectionStatus.Text = "טעינת הקשר נכשלה: " + ex.Message;
            RefreshConnection();
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
