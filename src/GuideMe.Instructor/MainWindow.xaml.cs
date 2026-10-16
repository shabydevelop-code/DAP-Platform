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

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            _capture.Start();
            _captureAvailable = true;
            StatusDetail.Text = "ערוץ הקלטה מקומי מוכן. חיבור תוסף Chrome עדיין לא הותקן.";
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
        StatusDetail.Text = "בצע פעולה באתר ולאחר מכן לחץ על ״סיים שלב״. חיבור להקלטת אירועי Chrome עדיין לא מומש.";
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
        StatusDetail.Text = "גבולות השלב נקלטו. אין עדיין נתוני פעולה או תוצאה מהדפדפן.";
        ReviewText.Text = $"התחלה: {_startedAt:HH:mm:ss}\nסיום: {endedAt:HH:mm:ss}\nפעולה: לא זוהתה (חיבור הדפדפן טרם מומש)\nתנאי השלמה: לא הוגדר";
        StartButton.IsEnabled = false;
        EndButton.IsEnabled = false;
        BubbleText.IsEnabled = true;
        ApproveButton.IsEnabled = false; // Never pretend a step can be persisted without a validated browser capture.
    }

    private void Approve_Click(object sender, RoutedEventArgs e)
    {
        // Reserved for the future validated V5 authoring transaction.
        MessageBox.Show("שמירה ל־V5 טרם מומשה.", "GuideMe Instructor");
    }
}
