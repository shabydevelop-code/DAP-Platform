using System;
using System.Windows;

namespace GuideMe.Instructor;

public partial class MainWindow : Window
{
    private enum RecordingState { Ready, Recording, Review }
    private RecordingState _state = RecordingState.Ready;
    private int _stepNumber = 1;
    private DateTimeOffset? _startedAt;

    public MainWindow() => InitializeComponent();

    private void StartStep_Click(object sender, RoutedEventArgs e)
    {
        if (_state != RecordingState.Ready || string.IsNullOrWhiteSpace(GuideKey.Text))
            return;

        _startedAt = DateTimeOffset.Now;
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
