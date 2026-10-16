using System;
using System.Windows;

namespace GuideMe.Instructor;

public partial class MainWindow : Window
{
    private enum RecordingState { Ready, Recording, Review }
    private RecordingState _state = RecordingState.Ready;
    private int _stepNumber = 1;
    private DateTimeOffset? _startedAt;
    private readonly BrowserCaptureServer _capture = new();
    private bool _captureAvailable;
    private BrowserCaptureEvent? _proposedField;
    private readonly System.Collections.Generic.List<string> _approvedDrafts = new();

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
        Closed += (_, _) => _capture.Dispose();
    }

    private void StartStep_Click(object sender, RoutedEventArgs e)
    {
        if (!_captureAvailable || _state != RecordingState.Ready || string.IsNullOrWhiteSpace(GuideKey.Text))
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
        reviewLines.Add("יתר ההגדרות (הקשר יישומי, בועית, מזהי שלב): דורשות הגדרת עורך.");

        ReviewText.Text = string.Join(Environment.NewLine, reviewLines);
        CopyReviewButton.IsEnabled = true;
        StartButton.IsEnabled = false;
        EndButton.IsEnabled = false;
        BubbleText.IsEnabled = true;
        ContextIdText.IsEnabled = true;
        ContextKindText.IsEnabled = true;
        ContextValueText.IsEnabled = true;
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
        if (string.IsNullOrWhiteSpace(ContextIdText.Text) ||
            string.IsNullOrWhiteSpace(ContextKindText.Text) ||
            string.IsNullOrWhiteSpace(ContextValueText.Text) ||
            string.IsNullOrWhiteSpace(BubbleText.Text))
        {
            MessageBox.Show("יש להשלים את פרטי השלב לפני אישור הטיוטה.", "GuideMe Instructor");
            return;
        }
        // Explicitly approve a session-only draft. No database mutation or claim of persistence.
        var field = _proposedField;
        var locator = !string.IsNullOrWhiteSpace(field.ElementId)
            ? "[id='" + field.ElementId + "']"
            : "[name='" + field.Name + "']";
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
        _stepNumber++;
        _proposedField = null;
        _state = RecordingState.Ready;
        StateLabel.Text = $"מוכן לתחילת שלב {_stepNumber}";
        StatusDetail.Text = "הטיוטה אושרה בזיכרון בלבד. לא נשמר דבר במסד הנתונים.";
        ReviewText.Text = string.Join(Environment.NewLine + Environment.NewLine, _approvedDrafts);
        CopyReviewButton.IsEnabled = true;
        StartButton.IsEnabled = true;
        EndButton.IsEnabled = false;
        ApproveButton.IsEnabled = false;
        BubbleText.IsEnabled = false;
        ContextIdText.IsEnabled = false;
        ContextKindText.IsEnabled = false;
        ContextValueText.IsEnabled = false;
        BubbleText.Clear();
    }
}
