using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached checks for retained drafts before preparation and EQ target changes.</summary>
internal static class PendingNumericEntryVerification
{
    internal static void Run(Action<bool, string> require)
    {
        CheckWorkspacePreparation(require);
        CheckReadOnlyText(require);
        CheckPendingNames(require);
        CheckPracticeBandSelection(require);
    }

    private static void CheckWorkspacePreparation(Action<bool, string> require)
    {
        var edits = new List<double>();
        var first = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, edits.Add);
        var second = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, edits.Add);
        var outside = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, edits.Add);
        var root = new StackPanel(); root.Children.Add(first); root.Children.Add(second);
        var firstEntry = Descendants<TextBox>(first).Single();
        var secondEntry = Descendants<TextBox>(second).Single();
        Descendants<TextBox>(outside).Single().Text = "outside draft";
        firstEntry.Text = "invalid retained draft";
        firstEntry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, firstEntry, null)
            { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        secondEntry.Text = (-9.875).ToString("G", CultureInfo.CurrentCulture) + " dB";
        require(!firstEntry.IsKeyboardFocusWithin && first.HasPendingEntry
            && !UpdateEditorCommit.TryCommitWorkspace(root) && firstEntry.Text == "invalid retained draft"
            && AutomationProperties.GetItemStatus(firstEntry).Contains("Invalid value")
            && first.Value == 0 && second.Value == 0 && edits.Count == 0,
            "Preparation finds an unfocused invalid numeric draft inside the live workspace and stops before applying later editors.");
        firstEntry.Text = (-7.125).ToString("G", CultureInfo.CurrentCulture) + " dB";
        require(UpdateEditorCommit.TryCommitWorkspace(root) && first.Value == -7.125 && second.Value == -9.875
            && edits.SequenceEqual([-7.125, -9.875]) && !first.HasPendingEntry && !second.HasPendingEntry
            && outside.HasPendingEntry && outside.Value == 0,
            "Corrected pending values commit exactly once before preparation, while drafts outside that workspace remain untouched.");
        require(UpdateEditorCommit.TryCommitWorkspace(root) && edits.Count == 2,
            "Repeated preparation does not apply completed numeric edits again.");

        second.IsEnabled = false;
        secondEntry.Text = "-18 dB";
        require(!UpdateEditorCommit.TryCommitWorkspace(root) && second.Value == -9.875 && edits.Count == 2,
            "Preparation cannot commit a pending value through a disabled numeric control.");
        second.CancelPendingEntry();
        require(UpdateEditorCommit.TryCommitWorkspace(root) && edits.Count == 2
            && PresentationSource.FromVisual(root) is null && PresentationSource.FromVisual(outside) is null,
            "A disabled editor with no pending edit is harmless; preparation checks use no native window, engine or saved settings.");
    }

    private static void CheckReadOnlyText(Action<bool, string> require)
    {
        var model = new TextDraft();
        var entry = new TextBox();
        entry.SetBinding(TextBox.TextProperty, new Binding(nameof(TextDraft.Value))
            { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit });
        entry.Text = "changed"; entry.IsReadOnly = true;
        require(UpdateEditorCommit.TryCommit(entry) && model.Value == "original",
            "The preparation path leaves a read-only bound text source unchanged.");
        entry.IsReadOnly = false; entry.IsEnabled = false;
        require(UpdateEditorCommit.TryCommit(entry) && model.Value == "original",
            "The preparation path leaves a disabled bound text source unchanged.");
        entry.IsEnabled = true;
        require(UpdateEditorCommit.TryCommit(entry) && model.Value == "changed",
            "An enabled editable bound text field retains its existing explicit commit behavior.");
    }

    private static void CheckPendingNames(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        var fixture = UiPreview.CreateSnapshot([]);
        var node = GraphDefaults.NewNode("gain", 40, 80); node.Id = "pending-name-node";
        fixture.Configuration.Graph = new GraphConfiguration
        {
            Nodes = [node], Frames = [new() { Id = "pending-name-frame", Name = "Original group", NodeIds = [node.Id] }]
        };
        mixer.ApplyPreviewSnapshot(fixture);
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false) { Width = 900, Height = 650 };
        workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        try
        {
            workspace.Measure(new Size(900, 650)); workspace.Arrange(new Rect(0, 0, 900, 650)); workspace.UpdateLayout();
            var view = Descendants<GraphNodeView>(workspace).Single();
            var nameEntry = Descendants<TextBox>(view).Single(entry => AutomationProperties.GetName(entry) == "Node name");
            nameEntry.Visibility = Visibility.Visible; nameEntry.Text = "Renamed node";
            var frame = workspace.FrameViews["pending-name-frame"];
            frame.BeginRename(name => workspace.RenameFrame("pending-name-frame", name));
            var frameEntry = Descendants<TextBox>(frame).Single(); frameEntry.Text = "Renamed group";
            require(UpdateEditorCommit.TryCommitWorkspace(workspace)
                && mixer.GraphConfig.Nodes.Single().Name == "Renamed node"
                && mixer.GraphConfig.Frames.Single().Name == "Renamed group"
                && mixer.SerializeUiPreferences().Contains("Renamed group", StringComparison.Ordinal)
                && !view.HasPendingRename && !frame.IsRenaming,
                "Quit/update preparation commits both unbound name editors before maintenance can cancel them, including the saved group preference.");
            int history = mixer.EditHistory.Entries.Count;
            require(UpdateEditorCommit.TryCommitWorkspace(workspace) && mixer.EditHistory.Entries.Count == history,
                "Repeated preparation does not duplicate completed node or group rename history.");
            frame.BeginRename(name => workspace.RenameFrame("pending-name-frame", name)); frameEntry.Text = "Focused group rename";
            require(UpdateEditorCommit.TryCommit(frameEntry) && mixer.GraphConfig.Frames.Single().Name == "Focused group rename",
                "The focused-editor path also commits an unbound group name through its actual rename callback.");
            nameEntry.Visibility = Visibility.Visible; nameEntry.Text = "Focused node rename";
            require(UpdateEditorCommit.TryCommit(nameEntry) && mixer.GraphConfig.Nodes.Single().Name == "Focused node rename",
                "The focused-editor path also commits an unbound node name through its actual rename callback.");
        }
        finally { workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    private static void CheckPracticeBandSelection(Action<bool, string> require)
    {
        using var lesson = new HelpAdvancedLessons.EqualizerLesson();
        lesson.View.Measure(new Size(700, double.PositiveInfinity));
        lesson.View.Arrange(new Rect(new Point(), lesson.View.DesiredSize)); lesson.View.UpdateLayout();
        lesson.Plot.SelectBand(1);
        var gainEntry = Descendants<TextBox>(lesson.Gain).Single();
        var widthEntry = Descendants<TextBox>(lesson.Width).Single();
        gainEntry.Text = "invalid practice gain";
        lesson.Gain.HandleEntryKey(Key.Tab, ModifierKeys.None);
        widthEntry.Text = 1.875.ToString(CultureInfo.CurrentCulture);
        lesson.Plot.SelectBand(2);
        require(!lesson.Gain.HasPendingEntry && !lesson.Width.HasPendingEntry
            && lesson.Gain.Value == 0 && lesson.Width.Value == 1
            && gainEntry.Text != "invalid practice gain"
            && lesson.Plot.SnapshotBands().All(band => band.GainDb == 0),
            "Practice EQ selection also cancels old drafts when the new band's gain and Q equal the old values.");
        gainEntry.Text = "invalid removed practice band";
        lesson.Gain.HandleEntryKey(Key.Tab, ModifierKeys.None);
        require(lesson.Plot.RemoveSelectedBand() && !lesson.Gain.HasPendingEntry
            && gainEntry.Text != "invalid removed practice band"
            && lesson.Plot.SnapshotBands().All(band => band.GainDb == 0)
            && PresentationSource.FromVisual(lesson.View) is null,
            "Removing a practice EQ band clears its pending draft before its index is reused, without touching the live workspace.");
    }

    private sealed class TextDraft
    {
        public string Value { get; set; } = "original";
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
