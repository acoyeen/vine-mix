using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private UpdateController? _updates;
    private Window? _updateDialog;
    internal void StopUpdateChecks()
    {
        _mixer.AutomaticUpdatePreferenceChanged -= AutomaticUpdatePreferenceChanged;
        _automaticUpdates?.Dispose();
        _updates?.Dispose();
    }
    internal bool TryPrepareUpdateEditors()
    {
        if (Workspace.HasActiveGesture)
        { Workspace.SetMessage("Finish moving or connecting the node before updating.", true); return false; }
        if (UpdateEditorCommit.TryCommitWorkspace(Workspace)) return true;
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
        var dialog = WorkspaceDialog.Create(this, "VineMix · Updates", 500, () => !controller.IsInstalling);
        dialog.DataContext = controller;
        dialog.Content = CreateUpdateContent(controller, dialog.Close, async () =>
        {
            if (controller.Download is null || _mixer.IsPreview) return;
            controller.SetInstalling(true, "Saving your workspace and preparing the update…");
            try { await ((App)Application.Current).InstallUpdateAsync(controller.Download); }
            catch (Exception error) { controller.Report("The update did not finish. " + error.Message); }
            finally { controller.SetInstalling(false); }
        }, _mixer);
        dialog.Closing += (_, args) => { if (controller.IsInstalling) args.Cancel = true; else controller.Cancel(); };
        dialog.Closed += (_, _) => _updateDialog = null;
        _updateDialog = dialog;
        dialog.Show();
        _ = controller.CheckAsync();
    }
    internal static FrameworkElement CreateUpdateContent(UpdateController controller, Action close, Func<Task> install,
        MixerViewModel? preferences = null)
    {
        var content = new StackPanel { Margin = new Thickness(24), DataContext = controller };
        content.Children.Add(new TextBlock { Text = "Updates", FontSize = 22, FontWeight = FontWeights.SemiBold });
        var installed = new TextBlock { Text = "Installed · " + controller.CurrentVersion,
            FontSize = 12, Margin = new Thickness(0, 5, 0, 24) };
        installed.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        content.Children.Add(installed);

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 };
        status.SetBinding(TextBlock.TextProperty, new Binding(nameof(UpdateController.Message)));
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(status, "Update status");
        content.Children.Add(status);
        var progress = new ProgressBar { IsIndeterminate = true, Height = 3, BorderThickness = new Thickness(0),
            Margin = new Thickness(0, 14, 0, 0) };
        progress.SetResourceReference(ForegroundProperty, "ConsoleABrush");
        progress.SetResourceReference(BackgroundProperty, "ConsoleBorderBrush");
        progress.SetBinding(VisibilityProperty, new Binding(nameof(UpdateController.IsBusy)) { Converter = new BooleanToVisibilityConverter() });
        content.Children.Add(progress);

        var notesSection = new StackPanel { Margin = new Thickness(0, 22, 0, 0) };
        AutomationProperties.SetName(notesSection, "Release details");
        var notesLabel = new TextBlock { Text = "What's new", FontSize = 12, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8) };
        notesLabel.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        notesSection.Children.Add(notesLabel);
        var notes = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180,
            BorderThickness = new Thickness(0), Padding = new Thickness(0), MinHeight = 0,
            Background = System.Windows.Media.Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        notes.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        notes.SetBinding(TextBox.TextProperty, new Binding(nameof(UpdateController.Notes)) { Mode = BindingMode.OneWay });
        AutomationProperties.SetName(notes, "Release notes");
        notesSection.Children.Add(notes);
        var notesStyle = new Style(typeof(StackPanel));
        var noNotes = new DataTrigger { Binding = new Binding(nameof(UpdateController.Notes)), Value = "" };
        noNotes.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        notesStyle.Triggers.Add(noNotes); notesSection.Style = notesStyle;
        content.Children.Add(notesSection);

        if (preferences is not null)
        {
            var automatic = new CheckBox { Content = "Check automatically", HorizontalAlignment = HorizontalAlignment.Left };
            automatic.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(nameof(MixerViewModel.AutomaticallyCheckForUpdates)) { Source = preferences, Mode = BindingMode.TwoWay });
            var automaticStyle = new Style(typeof(CheckBox), (Style)Application.Current.FindResource(typeof(CheckBox)));
            var installingPreference = new DataTrigger { Binding = new Binding(nameof(UpdateController.IsInstalling)), Value = true };
            installingPreference.Setters.Add(new Setter(IsEnabledProperty, false));
            automaticStyle.Triggers.Add(installingPreference); automatic.Style = automaticStyle;
            const string automaticHelp = "Checks daily while VineMix is running. Failed checks retry automatically. You choose when to download and install.";
            AutomationProperties.SetName(automatic, "Check for updates automatically");
            AutomationProperties.SetHelpText(automatic, automaticHelp);
            var explanation = new TextBlock { Text = "Checks daily. You choose when to download and install.",
                FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(25, 2, 0, 0) };
            explanation.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
            var preferencePanel = new StackPanel(); preferencePanel.Children.Add(automatic); preferencePanel.Children.Add(explanation);
            var preferenceSection = new Border { BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(0, 16, 0, 0), Child = preferencePanel };
            preferenceSection.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
            content.Children.Add(preferenceSection);
        }

        var footer = new DockPanel { Margin = new Thickness(0, 22, 0, 0), LastChildFill = true };
        var later = WorkspaceDialog.CloseButton(() => { if (!controller.IsInstalling) close(); });
        var closeStyle = new Style(typeof(Button), (Style)Application.Current.FindResource(typeof(Button)));
        var installing = new DataTrigger { Binding = new Binding(nameof(UpdateController.IsInstalling)), Value = true };
        installing.Setters.Add(new Setter(IsEnabledProperty, false)); closeStyle.Triggers.Add(installing); later.Style = closeStyle;
        DockPanel.SetDock(later, Dock.Left); footer.Children.Add(later);
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(actions); content.Children.Add(footer);

        Button Action(string label, string enabled, Func<Task> action, bool primary,
            params (string Property, bool Value)[] visibleWhen)
        {
            var button = new Button { Content = label, Padding = new Thickness(13, 7, 13, 7),
                MinHeight = 34, Margin = new Thickness(8, 0, 0, 0) };
            var style = new Style(typeof(Button), (Style)Application.Current.FindResource(typeof(Button)));
            style.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
            var show = new MultiDataTrigger();
            foreach (var condition in visibleWhen) show.Conditions.Add(new System.Windows.Condition(new Binding(condition.Property), condition.Value));
            show.Setters.Add(new Setter(VisibilityProperty, Visibility.Visible)); style.Triggers.Add(show);
            if (primary)
            {
                style.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("BrandActionBrush")));
                style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("BrandActionTextBrush")));
                style.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("BrandActionBrush")));
            }
            if (label == "Check for updates")
            {
                var offerAvailable = new DataTrigger { Binding = new Binding(nameof(UpdateController.HasOffer)), Value = true };
                offerAvailable.Setters.Add(new Setter(BackgroundProperty, System.Windows.Media.Brushes.Transparent));
                offerAvailable.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("ConsoleMutedBrush")));
                offerAvailable.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(0)));
                style.Triggers.Add(offerAvailable);
            }
            button.Style = style;
            button.SetBinding(IsEnabledProperty, new Binding(enabled));
            AutomationProperties.SetName(button, label);
            button.Click += async (_, _) => await action();
            actions.Children.Add(button); return button;
        }
        Action("Check for updates", nameof(UpdateController.CanCheck), async () => await controller.CheckAsync(), true,
            (nameof(UpdateController.IsBusy), false));
        Action("Download update", nameof(UpdateController.CanDownload), controller.DownloadAsync, true,
            (nameof(UpdateController.IsBusy), false), (nameof(UpdateController.HasOffer), true), (nameof(UpdateController.HasDownloadedUpdate), false));
        var apply = Action("Install and restart", nameof(UpdateController.CanInstall), install, true,
            (nameof(UpdateController.IsBusy), false), (nameof(UpdateController.HasDownloadedUpdate), true));
        apply.ToolTip = "Save your workspace, briefly stop audio, install the verified update and reopen VineMix.";
        AutomationProperties.SetHelpText(apply, (string)apply.ToolTip);
        var cancel = Action("Cancel", nameof(UpdateController.CanCancel), () => { controller.Cancel(); return Task.CompletedTask; }, false,
            (nameof(UpdateController.CanCancel), true));
        AutomationProperties.SetHelpText(cancel, "Cancel the update check or download. Your current version keeps running.");
        return content;
    }

    internal Task BeginAutomaticUpdateCheckAsync()
    {
        InitializeAutomaticUpdates();
        return Task.CompletedTask;
    }
}
