using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;

namespace GuideMe.Instructor;

public partial class MainWindow : Window
{
    private enum RecordingState { Ready, Recording, Review }
    private RecordingState _state = RecordingState.Ready;
    private int _stepNumber = 1;
    private DateTimeOffset? _startedAt;
    private readonly BrowserCaptureServer _capture = new();
    private bool _captureAvailable;
    private readonly System.Windows.Threading.DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private BrowserCaptureEvent? _proposedField;
    private readonly System.Collections.Generic.List<string> _approvedDrafts = new();

    private sealed record StepListItem(int Number, string Title, string Subtitle, string Review, string? StepId = null, string? Bubble = null);
    private V5GuideReader.GuideInfo? _loadedGuide;
    private readonly System.Collections.Generic.List<StepListItem> _stepItems = new();

    private void RefreshStepList()
    {
        var query = StepSearch.Text.Trim();
        var selectedNumber = (StepsList.SelectedItem as StepListItem)?.Number;
        StepsList.ItemsSource = _stepItems.Where(item =>
            query.Length == 0 || item.Number.ToString().Contains(query, StringComparison.OrdinalIgnoreCase) ||
            item.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            item.Review.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
        StepCountLabel.Text = _loadedGuide is null
            ? "לא נבחרה לומדה"
            : _stepItems.Count == 0 ? "בלומדה זו עדיין אין שלבים" : $"{_loadedGuide.Name} · {_stepItems.Count} שלבים";
        if (selectedNumber is not null)
            StepsList.SelectedItem = ((System.Collections.Generic.IEnumerable<StepListItem>)StepsList.ItemsSource)
                .FirstOrDefault(item => item.Number == selectedNumber);
    }

    private void StepSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (StepsList is not null && StepCountLabel is not null)
            RefreshStepList();
    }

    private void StepsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StepsList.SelectedItem is not StepListItem selected || _state != RecordingState.Ready)
            return;
        ReviewHeading.Text = $"שלב {selected.Number} — {selected.Title}";
        ReviewText.Text = selected.Review;
        CopyReviewButton.IsEnabled = true;
        var existing = _loadedGuide is not null && selected.StepId is not null;
        CaptureReviewPanel.Visibility = existing ? Visibility.Collapsed : Visibility.Visible;
        DraftBubbleHeading.Visibility = existing ? Visibility.Collapsed : Visibility.Visible;
        BubbleText.Visibility = existing ? Visibility.Collapsed : Visibility.Visible;
        if (existing)
            ShowLoadedStep(selected.StepId!);
    }

    private void SetNewGuideMode(bool creating)
    {
        NewGuidePanel.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceTabs.Visibility = creating ? Visibility.Collapsed : Visibility.Visible;
        StepsSidebar.Visibility = creating ? Visibility.Hidden : Visibility.Visible;
        CurrentGuideLabel.Text = creating ? "יצירת לומדה חדשה" :
            _loadedGuide is null ? "לא נבחרה לומדה" : $"פתוחה: {_loadedGuide.Name}";
    }

    private void ClearWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Ready)
        {
            MessageBox.Show("יש לסיים את ההקלטה או סקירת הטיוטה לפני ניקוי הלוח.", "GuideMe Instructor");
            return;
        }

        _loadedGuide = null;
        _stepItems.Clear();
        _approvedDrafts.Clear();
        _proposedField = null;
        _stepNumber = 1;
        _startedAt = null;
        ExistingGuides.SelectedIndex = -1;
        StepSearch.Clear();
        StepsList.SelectedItem = null;
        GuideKey.Clear();
        EditableGuideName.Clear();
        EditableGuideName.IsEnabled = false;
        SaveGuideButton.IsEnabled = false;
        ContextsDetails.Text = "לא נטענה לומדה.";
        BubbleDetails.Text = "לא נבחר שלב.";
        ElementDetails.Text = "לא נבחר שלב.";
        AnchorsDetails.Text = "לא נבחר שלב.";
        InteractionDetails.Text = "לא נבחר שלב.";
        RulesDetails.Text = "לא נבחר שלב.";
        ReviewText.Clear();
        BubbleText.Clear();
        CaptureReviewPanel.Visibility = Visibility.Collapsed;
        DraftBubbleHeading.Visibility = Visibility.Collapsed;
        BubbleText.Visibility = Visibility.Collapsed;
        ApproveButton.IsEnabled = false;
        CopyReviewButton.IsEnabled = false;
        EndButton.IsEnabled = false;
        StateLabel.Text = "מוכן לתחילת שלב 1";
        SetNewGuideMode(false);
        WorkspaceTabs.SelectedIndex = 0;
        GuideSettingsTabs.SelectedIndex = 0;
        DatabaseStatus.Text = "הלוח נוקה. לא נמחקו נתונים ממסד הנתונים.";
        RefreshStepList();
    }

    private void CancelNewGuide_Click(object sender, RoutedEventArgs e)
    {
        SetNewGuideMode(false);
    }

    private void NewGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Ready)
        {
            MessageBox.Show("יש לסיים את הקלטת השלב לפני יצירת לומדה.", "GuideMe Instructor");
            return;
        }
        WorkspaceTabs.SelectedIndex = 0;
        GuideSettingsTabs.SelectedIndex = 0;
        NewGuideName.Clear();
        SetNewGuideMode(true);
        NewGuideName.Focus();
    }

    private void CreateGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Ready)
        {
            MessageBox.Show("יש לסיים את ההקלטה לפני יצירת לומדה.", "GuideMe Instructor");
            return;
        }
        try
        {
            var created = V5GuideWriter.CreateGuide(NewGuideName.Text);
            ExistingGuides.ItemsSource = V5GuideReader.ListGuides();
            ExistingGuides.SelectedItem = ((System.Collections.Generic.IEnumerable<V5GuideReader.GuideInfo>)ExistingGuides.ItemsSource)
                .FirstOrDefault(g => g.Id == created.Id);
            _loadedGuide = null;
            LoadExistingGuide_Click(sender, e);
            WorkspaceTabs.SelectedIndex = 0;
            GuideSettingsTabs.SelectedIndex = 0;
            SetNewGuideMode(false);
            DatabaseStatus.Text = $"נוצרה לומדה חדשה (GuideId={created.Id}) ללא שלבים או הקשרי אפליקציה.";
            MessageBox.Show($"הלומדה '{created.Name}' נוצרה בהצלחה.\nGuideId: {created.Id}\nלא נוצרו שלבים או הקשרי אפליקציה.",
                "GuideMe Instructor");
        }
        catch (Exception ex)
        {
            MessageBox.Show("יצירת הלומדה נכשלה: " + ex.Message, "GuideMe Instructor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedGuide is null)
            return;
        try
        {
            var updated = V5GuideWriter.RenameGuide(_loadedGuide.Id, EditableGuideName.Text);
            _loadedGuide = updated;
            ExistingGuides.ItemsSource = V5GuideReader.ListGuides();
            ExistingGuides.SelectedItem = ((System.Collections.Generic.IEnumerable<V5GuideReader.GuideInfo>)ExistingGuides.ItemsSource)
                .FirstOrDefault(g => g.Id == updated.Id);
            CurrentGuideLabel.Text = $"פתוחה: {updated.Name}";
            DatabaseStatus.Text = "שם הלומדה נשמר.";
            RefreshStepList();
        }
        catch (Exception ex)
        {
            MessageBox.Show("שמירת שם הלומדה נכשלה: " + ex.Message, "GuideMe Instructor",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadExistingGuide_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Ready)
        {
            MessageBox.Show("יש לסיים את ההקלטה או סקירת הטיוטה לפני טעינת לומדה.", "GuideMe Instructor");
            return;
        }
        if (ExistingGuides.SelectedItem is not V5GuideReader.GuideInfo guide)
            return;
        try
        {
            var steps = V5GuideReader.ListSteps(guide.Id);
            _loadedGuide = guide;
            CurrentGuideLabel.Text = $"פתוחה: {guide.Name}";
            SetNewGuideMode(false);
            EditableGuideName.Text = guide.Name;
            EditableGuideName.IsEnabled = true;
            SaveGuideButton.IsEnabled = true;
            ContextsDetails.Text = V5GuideReader.ReadTable(guide.Id, "ApplicationContexts");
            WorkspaceTabs.SelectedIndex = 0;
            GuideSettingsTabs.SelectedIndex = 0;
            CaptureReviewPanel.Visibility = Visibility.Collapsed;
            DraftBubbleHeading.Visibility = Visibility.Collapsed;
            BubbleText.Visibility = Visibility.Collapsed;
            BubbleText.IsEnabled = false;
            _stepItems.Clear();
            foreach (var step in steps)
            {
                var title = string.IsNullOrWhiteSpace(step.Bubble) ? step.Id : step.Bubble.Trim().Replace("\r", " ").Replace("\n", " ");
                if (title.Length > 58)
                    title = title[..58] + "…";
                _stepItems.Add(new StepListItem(step.Order, title,
                    step.Enabled ? "פעיל · V5 · קריאה בלבד" : "לא פעיל · V5 · קריאה בלבד",
                    $"StepId: {step.Id}\nStepOrder: {step.Order}\nBubble: {step.Bubble}", step.Id, step.Bubble));
            }
            StepSearch.Clear();
            GuideKey.Text = guide.Key;
            GuideKey.IsEnabled = false;
            DatabaseStatus.Text = $"נטענו {steps.Count} שלבים. קריאה בלבד.";
            StateLabel.Text = "לומדה קיימת — מצב עיון";
            StatusDetail.Text = "עריכת שלבים בלומדה קיימת עדיין אינה זמינה.";
            StartButton.IsEnabled = false;
            RefreshStepList();
            if (_stepItems.Count > 0)
                StepsList.SelectedItem = _stepItems[0];
        }
        catch (Exception ex)
        {
            DatabaseStatus.Text = "טעינת הלומדה נכשלה: " + ex.Message;
        }
    }

    private void ShowLoadedStep(string stepId)
    {
        if (_loadedGuide is null)
            return;
        try
        {
            var id = _loadedGuide.Id;
            BubbleDetails.Text = V5GuideReader.ReadTable(id, "Steps", stepId);
            ElementDetails.Text = V5GuideReader.ReadTable(id, "Elements", stepId);
            AnchorsDetails.Text = V5GuideReader.ReadTable(id, "ElementAnchors", stepId);
            InteractionDetails.Text = V5GuideReader.ReadTable(id, "StepTargets", stepId);
            RulesDetails.Text = V5GuideReader.ReadTable(id, "StepRules", stepId);
        }
        catch (Exception ex)
        {
            DatabaseStatus.Text = "קריאת פרטי השלב נכשלה: " + ex.Message;
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _capture.Start();
            PairingToken.Text = _capture.SessionToken;
            _captureAvailable = true;
            StatusDetail.Text = "ערוץ ההקלטה מוכן. חבר את התוסף באמצעות קוד החיבור.";
            _capture.EventCaptured += _ => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_state == RecordingState.Recording)
                    StatusDetail.Text = "התקבל אירוע מהדפדפן בערוץ ההקלטה.";
            }));
        }
        catch (Exception ex)
        {
            StatusDetail.Text = "לא ניתן לפתוח את ערוץ ההקלטה המקומי: " + ex.Message;
            StartButton.IsEnabled = false;
        }
        try
        {
            ExistingGuides.ItemsSource = V5GuideReader.ListGuides();
            DatabaseStatus.Text = "מסד V5 נפתח לקריאה בלבד.";
        }
        catch (Exception ex)
        {
            DatabaseStatus.Text = "לא ניתן לקרוא את V5: " + ex.Message;
        }
        RefreshStepList();
        RefreshConnection();
        _connectionTimer.Tick += (_, _) => RefreshConnection();
        _connectionTimer.Start();
        Closed += (_, _) => { _connectionTimer.Stop(); _capture.Dispose(); };
    }

    private void RefreshConnection()
    {
        var connected = _captureAvailable && _capture.IsConnected;
        ConnectionPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ConnectionIndicator.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ConnectionIndicator.Text = connected ? "● התוסף מחובר" : "התוסף אינו מחובר";
        if (_state == RecordingState.Ready)
        {
            StartButton.IsEnabled = connected && _loadedGuide is null;
            StatusDetail.Text = connected ? "התוסף מחובר ומאומת. ניתן להתחיל שלב." : "התוסף אינו מחובר או שהחיבור פג. חבר מחדש את התוסף.";
        }
        else if (_state == RecordingState.Recording && !connected)
        {
            _capture.EndStep();
            _state = RecordingState.Ready;
            _startedAt = null;
            EndButton.IsEnabled = false;
            StartButton.IsEnabled = false;
            StateLabel.Text = $"הקלטת שלב {_stepNumber} הופסקה";
            StatusDetail.Text = "החיבור לתוסף אבד. ההקלטה הופסקה ללא אישור טיוטה; חבר מחדש ונסה שוב.";
        }
    }

    private void StartStep_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedGuide is not null || !_captureAvailable || !_capture.IsConnected || _state != RecordingState.Ready || string.IsNullOrWhiteSpace(GuideKey.Text))
            return;

        _startedAt = DateTimeOffset.Now;
        _capture.BeginStep();
        _state = RecordingState.Recording;
        StateLabel.Text = $"מקליט גבולות שלב {_stepNumber}";
        StatusDetail.Text = "בצע פעולה באתר ולחץ על ״סיים שלב״.";
        StartButton.IsEnabled = false;
        EndButton.IsEnabled = true;
        GuideKey.IsEnabled = false;
    }

    private void EndStep_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Recording || _startedAt is null)
            return;

        var endedAt = DateTimeOffset.Now;
        var events = _capture.EndStep();
        _state = RecordingState.Review;
        StateLabel.Text = $"סקירת שלב {_stepNumber}";
        StatusDetail.Text = events.Count > 0 ? "נקלטו פעולות מהדפדפן." : "לא נקלטו פעולות. בדוק את חיבור התוסף.";
        var reviewLines = new System.Collections.Generic.List<string>
        {
            $"התחלה: {_startedAt:HH:mm:ss}",
            $"סיום: {endedAt:HH:mm:ss}",
            $"אירועים שנקלטו: {events.Count}",
            ""
        };
        foreach (var (capturedEvent, index) in events.Take(10).Select((item, index) => (item, index)))
        {
            reviewLines.Add($"פעולה {index + 1}");
            reviewLines.Add($"סוג: {capturedEvent.Kind}");
            reviewLines.Add($"רכיב: {capturedEvent.Tag}");
            reviewLines.Add($"תווית: {capturedEvent.Label}");
            reviewLines.Add($"מזהה: {capturedEvent.ElementId ?? "לא זוהה"}");
            reviewLines.Add($"כתובת: {capturedEvent.Url}");
            if ((capturedEvent.Kind == "blur" || capturedEvent.Kind == "change") && !string.IsNullOrEmpty(capturedEvent.Value))
                reviewLines.Add($"ערך השדה בזמן האירוע: {capturedEvent.Value}");
            if (!string.IsNullOrEmpty(capturedEvent.Before) && !string.IsNullOrEmpty(capturedEvent.After))
            {
                using var before = System.Text.Json.JsonDocument.Parse(capturedEvent.Before);
                using var after = System.Text.Json.JsonDocument.Parse(capturedEvent.After);
                reviewLines.Add("השוואת מצב הדף (לפני ← אחרי):");
                foreach (var property in new[] { "url", "dialogs", "inputs", "selects", "buttons", "headings", "dialogsLabels" })
                {
                    var oldValue = before.RootElement.GetProperty(property).ToString();
                    var newValue = after.RootElement.GetProperty(property).ToString();
                    if (oldValue != newValue)
                        reviewLines.Add($"  {property}: {oldValue} ← {newValue}");
                }
                if (before.RootElement.ToString() == after.RootElement.ToString())
                    reviewLines.Add("  לא זוהה שינוי במדדים המבניים שנאספו.");
                reviewLines.Add("הערה: השוואה ראשונית בלבד; אינה תנאי השלמה מאומת.");
            }
            else
                reviewLines.Add("מצב לפני/אחרי: לא נאסף (יש לטעון מחדש את תוסף Chrome).");
            reviewLines.Add("");
        }
        if (events.Count > 10)
            reviewLines.Add($"מוצגות 10 פעולות מתוך {events.Count}.");
        // Propose a field validation from captured behavior only, never from a reference guide.
        var fieldEvent = events.LastOrDefault(e =>
            (e.Kind == "blur" || e.Kind == "change") &&
            (e.Tag == "input" || e.Tag == "textarea" || e.Tag == "select") &&
            !string.IsNullOrEmpty(e.Value));
        _proposedField = fieldEvent;
        var candidate = fieldEvent?.ContextCandidate;
        var matchingObservations = string.IsNullOrWhiteSpace(candidate) ? 0 :
            events.Count(e => e.ContextCandidate == candidate && e.Url == fieldEvent!.Url && e.FrameSelector == fieldEvent.FrameSelector);
        var contextObservedRepeatedly = matchingObservations >= 2;
        reviewLines.Add("הגדרות מוצעות מתוך ההקלטה:");
        if (fieldEvent is not null)
        {
            string? locator = null;
            if (!string.IsNullOrWhiteSpace(fieldEvent.ElementId))
                locator = "[id='" + fieldEvent.ElementId.Replace("\\", "\\\\").Replace("'", "\\'") + "']";
            else if (!string.IsNullOrWhiteSpace(fieldEvent.Name))
                locator = "[name='" + fieldEvent.Name.Replace("\\", "\\\\").Replace("'", "\\'") + "']";
            reviewLines.Add($"Locator: {locator ?? "לא זוהה — נדרש בחירת רכיב"}");
            reviewLines.Add($"FrameContext: {(!string.IsNullOrWhiteSpace(fieldEvent.FrameSelector) ? fieldEvent.FrameSelector : "לא זוהה / מסמך ראשי")}");
            reviewLines.Add("SignalKind: value-equals (הצעה)");
            reviewLines.Add($"ExpectedValue: {fieldEvent.Value}");
            reviewLines.Add("PolicyKind: AutomaticOnValidation (הצעה, לא נלמד מהאירוע)");
            reviewLines.Add("הערה: ייחודיות ה-Locator ותנאי ההשלמה טרם אומתו.");
        }
        else
            reviewLines.Add("לא זוהה ערך שדה מתאים להפקת value-equals.");
        reviewLines.Add(contextObservedRepeatedly
            ? $"מועמד לזיהוי מסך: {candidate} (נצפה {matchingObservations} פעמים באותו מסמך; טרם נבדק מול מסכים אחרים)."
            : "לא זוהה סימן מסך חוזר מספיק. נדרש אימות נוסף לפני פרסום.");
        reviewLines.Add("טקסט הבועית ניתן לעריכה; הטיוטה אינה מוכנה לפרסום.");

        ReviewText.Text = string.Join(Environment.NewLine, reviewLines);
        CopyReviewButton.IsEnabled = true;
        StartButton.IsEnabled = false;
        EndButton.IsEnabled = false;
        BubbleText.IsEnabled = true;
        DraftBubbleHeading.Visibility = Visibility.Visible;
        BubbleText.Visibility = Visibility.Visible;
        CaptureReviewPanel.Visibility = Visibility.Visible;
        WorkspaceTabs.SelectedIndex = 1;
        StepDetailsTabs.SelectedIndex = 0;
        WorkspaceTabs.SelectedIndex = 1;
        StepDetailsTabs.SelectedIndex = 0; // Draft belongs to Steps.
        ContextIdText.Text = "unresolved";
        ContextKindText.Text = contextObservedRepeatedly ? "css-exists (candidate)" : "unverified";
        ContextValueText.Text = contextObservedRepeatedly ? candidate ?? "" : fieldEvent?.Url ?? "";
        ContextIdText.IsEnabled = false;
        ContextKindText.IsEnabled = false;
        ContextValueText.IsEnabled = false;
        ApproveButton.IsEnabled = fieldEvent is not null &&
            (!string.IsNullOrWhiteSpace(fieldEvent.ElementId) || !string.IsNullOrWhiteSpace(fieldEvent.Name));
    }

    private void CopyReview_Click(object sender, RoutedEventArgs e)
    {
        if (_state == RecordingState.Review && !string.IsNullOrWhiteSpace(ReviewText.Text))
            Clipboard.SetText(ReviewText.Text);
    }

    private void CopyPairingToken_Click(object sender, RoutedEventArgs e)
    {
        if (_captureAvailable)
            Clipboard.SetText(_capture.SessionToken);
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Review || _proposedField is null)
            return;
        if (string.IsNullOrWhiteSpace(BubbleText.Text))
        {
            MessageBox.Show("יש להזין טקסט בועית לפני אישור הטיוטה.", "GuideMe Instructor");
            return;
        }
        // Explicitly approve a session-only draft. No database mutation or claim of persistence.
        var field = _proposedField;
        var locator = !string.IsNullOrWhiteSpace(field.ElementId)
            ? "[id='" + field.ElementId + "']"
            : "[name='" + field.Name + "']";
        var approvedNumber = _stepNumber;
        _approvedDrafts.Add(string.Join(Environment.NewLine,
            $"GuideKey: {GuideKey.Text}",
            $"StepOrder: {_stepNumber}",
            $"ContextId: {ContextIdText.Text}",
            $"ContextKind: {ContextKindText.Text}",
            $"ContextValue: {ContextValueText.Text}",
            $"Locator: {locator}",
            $"FrameContext: {field.FrameSelector}",
            "SignalKind: value-equals",
            $"ExpectedValue: {field.Value}",
            "PolicyKind: AutomaticOnValidation (author-approved proposal)",
            $"Bubble: {BubbleText.Text}"));
        var approvedReview = _approvedDrafts[^1];
        var title = BubbleText.Text.Trim().Replace("\r", " ").Replace("\n", " ");
        if (title.Length > 58)
            title = title[..58] + "…";
        _stepItems.Add(new StepListItem(approvedNumber, title, "טיוטה מאושרת · לא נשמרה", approvedReview));
        _stepNumber++;
        _proposedField = null;
        _state = RecordingState.Ready;
        StateLabel.Text = $"מוכן לתחילת שלב {_stepNumber}";
        StatusDetail.Text = "טיוטה זמנית אושרה. ההקשר טרם אומת ולכן אינה מוכנה לפרסום. לא נשמר דבר במסד הנתונים.";
        ReviewHeading.Text = "סקירת השלב";
        ReviewText.Text = approvedReview;
        CaptureReviewPanel.Visibility = Visibility.Visible;
        RefreshStepList();
        StepsList.SelectedItem = _stepItems[^1];
        CopyReviewButton.IsEnabled = true;
        StartButton.IsEnabled = _capture.IsConnected;
        EndButton.IsEnabled = false;
        ApproveButton.IsEnabled = false;
        BubbleText.IsEnabled = false;
        ContextIdText.IsEnabled = false;
        ContextKindText.IsEnabled = false;
        ContextValueText.IsEnabled = false;
        BubbleText.Clear();
    }
}
