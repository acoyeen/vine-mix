using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private UpdateController? _updates;
    private Window? _updateDialog;
    internal void StopUpdateChecks() => _updates?.Dispose();
    internal bool TryPrepareUpdateEditors()
    {
        if (Workspace.HasActiveGesture)
        { Workspace.SetMessage("Finish moving or connecting the node before updating.", true); return false; }
        if (UpdateEditorCommit.TryCommitFocused()) return true;
        Workspace.SetMessage("Enter a valid value before updating. Your draft has been kept.", true);
        return false;
    }
    private void OpenUpdates()
    {
        if (!TryPrepareUpdateEditors()) return;
        _settingsMenu.IsOpen = false;
        if (_updateDialog is not null) { _updateDialog.Activate(); return; }
        _updates ??= new UpdateController(_mixer.IsPreview);
        var controller = _updates;
        var dialog = new Window { Owner = this, Title = "VineMix updates", Width = 540, MinWidth = 440,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = FontFamily, FontSize = FontSize,
            DataContext = controller };
        NativeCaption.Attach(dialog, 24, resizable: false);
        dialog.SetResourceReference(BackgroundProperty, "ConsoleWindowBrush");
        dialog.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        dialog.Content = CreateUpdateContent(controller, dialog.Close, async () =>
        {
            if (controller.Download is null || _mixer.IsPreview) return;
            controller.SetInstalling(true, "Saving your workspace and preparing the update…");
            try { await ((App)Application.Current).InstallUpdateAsync(controller.Download); }
            catch (Exception error) { controller.Report("The update did not finish. " + error.Message); }
            finally { controller.SetInstalling(false); }
        });
        dialog.Closing += (_, args) => { if (controller.IsInstalling) args.Cancel = true; else controller.Cancel(); };
        dialog.Closed += (_, _) => _updateDialog = null;
        _updateDialog = dialog;
        dialog.Show();
        _ = controller.CheckAsync();
    }
    internal static FrameworkElement CreateUpdateContent(UpdateController controller, Action close, Func<Task> install)
    {
        var content = new StackPanel { Margin = new Thickness(24), DataContext = controller };
        content.Children.Add(new TextBlock { Text = "VineMix updates", FontSize = 22, FontWeight = FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = "Current version: " + controller.CurrentVersion, Margin = new Thickness(0, 6, 0, 14) });
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MinHeight = 48, Margin = new Thickness(0, 0, 0, 10) };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(UpdateController.Message)));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(status, "Update status"); content.Children.Add(status);
        var progress = new ProgressBar { IsIndeterminate = true, Height = 4, Margin = new Thickness(0, 0, 0, 12) };
        progress.SetBinding(VisibilityProperty, new Binding(nameof(UpdateController.IsBusy)) { Converter = new BooleanToVisibilityConverter() });
        content.Children.Add(progress);
        var notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 16) };
        notes.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        notes.SetBinding(TextBox.TextProperty, new Binding(nameof(UpdateController.Notes)) { Mode = BindingMode.OneWay });
        AutomationProperties.SetName(notes, "Release notes"); content.Children.Add(notes);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Button Action(string label, string enabled, Func<Task> action)
        {
            var button = new Button { Content = label, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4, 3, 0, 0) };
            button.SetBinding(IsEnabledProperty, new Binding(enabled));
            button.Click += async (_, _) => await action();
            actions.Children.Add(button); return button;
        }
        Action("Check for updates", nameof(UpdateController.CanCheck), controller.CheckAsync);
        Action("Download update", nameof(UpdateController.CanDownload), controller.DownloadAsync);
        var apply = Action("Install and restart", nameof(UpdateController.CanInstall), install);
        apply.ToolTip = "Save your workspace, briefly stop audio, install the verified update and reopen VineMix.";
        AutomationProperties.SetHelpText(apply, (string)apply.ToolTip);
        Action("Cancel download", nameof(UpdateController.CanCancel), () => { controller.Cancel(); return Task.CompletedTask; });
        var later = new Button { Content = "Later", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(4, 3, 0, 0), IsCancel = true };
        later.Click += (_, _) => { if (!controller.IsInstalling) close(); };
        actions.Children.Add(later); content.Children.Add(actions);
        return content;
    }

    internal async Task BeginAutomaticUpdateCheckAsync()
    {
        if (_mixer.IsPreview) return;
        await Task.Delay(TimeSpan.FromSeconds(30));
        if (!_mixer.AutomaticallyCheckForUpdates || ((App)Application.Current).IsQuitting) return;
        string stamp = Path.Combine(UpdateTransaction.Root, "last-check.txt");
        try
        {
            if (File.Exists(stamp) && DateTimeOffset.TryParse(File.ReadAllText(stamp), out var last)
                && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(1)) return;
            UpdateFiles.CreatePrivateDirectory(UpdateTransaction.Root);
            File.WriteAllText(stamp, DateTimeOffset.UtcNow.ToString("o"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return; }
        _updates ??= new UpdateController(false);
        await _updates.CheckAsync();
        if (_updates.CanDownload) Workspace.SetMessage("A VineMix update is available. Open Settings → Check for updates.", false);
    }
}
