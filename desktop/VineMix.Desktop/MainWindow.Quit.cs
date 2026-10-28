using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private Task QuitFromSettingsAsync() => _mixer.IsPreview ? Task.CompletedTask : ((App)Application.Current).QuitAsync();

    internal bool TryPrepareQuitEditors()
    {
        if (Workspace.HasActiveGesture)
        { Workspace.SetMessage("Finish moving or connecting the node before quitting.", true); return false; }
        if (UpdateEditorCommit.TryCommitWorkspace(Workspace)) return true;
        Workspace.SetMessage("Enter a valid value before quitting. Your draft has been kept.", true);
        return false;
    }

    internal Task<QuitChoice> ChooseQuitRecoveryAsync(QuitSaveResult failure)
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        var choice = QuitChoice.Cancel;
        var dialog = new Window
        {
            Owner = this, Title = "VineMix · Changes not saved", Width = 540, MinWidth = 440,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = FontSize
        };
        dialog.SetResourceReference(BackgroundProperty, "ConsoleWindowBrush");
        dialog.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        dialog.Content = CreateQuitRecoveryContent(failure, selected => { choice = selected; dialog.Close(); });
        dialog.ShowDialog();
        return Task.FromResult(choice);
    }

    internal static FrameworkElement CreateQuitRecoveryContent(QuitSaveResult failure, Action<QuitChoice> choose)
    {
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Keep your unsaved changes", FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock
        {
            Text = "Your latest workspace changes have not been saved. VineMix is still open. Retry saving, keep a recovery copy before quitting, or return to the workspace.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 10)
        });
        var detail = new TextBlock { Text = failure.Error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleAlertTextBrush");
        AutomationProperties.SetLiveSetting(detail, AutomationLiveSetting.Polite);
        var details = new ScrollViewer { Content = detail, MaxHeight = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(details, "Save failure details");
        panel.Children.Add(details);
        Add("Retry saving", QuitChoice.Retry, "Try saving the queued workspace changes again.");
        Add("Save recovery copy and quit…", QuitChoice.RecoveryCopy,
            "Choose a new JSON file containing the complete workspace and queued edits. Quit only after the copy is saved. Existing files are never replaced.");
        Add("Cancel · keep VineMix open", QuitChoice.Cancel, "Keep the current session open and retain your pending changes.", cancel: true);
        Add("Quit without saving", QuitChoice.Discard, "Stop VineMix and discard changes that were not saved.", danger: true);
        return panel;

        void Add(string label, QuitChoice choice, string help, bool cancel = false, bool danger = false)
        {
            var button = new Button { Content = label, MinHeight = 34, Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 4, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left,
                IsCancel = cancel, IsDefault = cancel };
            if (danger) button.SetResourceReference(ForegroundProperty, "ConsoleMuteBrush");
            AutomationProperties.SetName(button, label); AutomationProperties.SetHelpText(button, help);
            button.Click += (_, _) => choose(choice);
            panel.Children.Add(button);
        }
    }

    internal Task<QuitRecoveryResult> SaveQuitRecoveryCopyAsync()
    {
        var picker = new SaveFileDialog
        {
            Title = "Save VineMix recovery copy", Filter = "VineMix workspace recovery (*.json)|*.json",
            DefaultExt = ".json", AddExtension = true, OverwritePrompt = false,
            FileName = "VineMix-recovery-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"
        };
        try
        {
            if (picker.ShowDialog(this) != true) return Task.FromResult(new QuitRecoveryResult(false, true, ""));
            _mixer.WriteQuitRecoveryCopy(picker.FileName);
            return Task.FromResult(new QuitRecoveryResult(true, false, ""));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(new QuitRecoveryResult(false, false,
                "Choose a writable location and a filename that does not already exist. " + error.Message));
        }
    }
}
