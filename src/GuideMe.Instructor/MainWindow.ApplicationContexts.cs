using System;
using System.Windows;

namespace GuideMe.Instructor;

public partial class MainWindow
{
    private void SelectApplicationWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedGuide is null) return;
        var url = _capture.ActiveWebUrl;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            TargetSelectionStatus.Text = "פתח את לשונית האתר וודא שהתוסף מחובר.";
            return;
        }
        SelectedRuntime.Text = "Web";
        SelectedContextId.Text = "web-main";
        SelectedMatcherProperty.Text = "UrlHost";
        SelectedMatcherValue.Text = uri.Host;
        TargetSelectionStatus.Text = "זוהה אתר. ניתן לשמור את ההקשר.";
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
