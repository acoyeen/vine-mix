using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached entry-to-graph checks, without native focus, keyboard input or engine access.</summary>
internal static class NumericTabCommitVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Numeric Tab history checks use only a detached preview graph.");
        var node = GraphDefaults.NewNode("mix", 40, 40);
        mixer.ApplyPreviewSnapshot(new() { Configuration = new() { Version = 2, Graph = new() { Nodes = [node] } }, Status = new() });
        await layout();
        var view = Descendants<GraphNodeView>(workspace).Single();
        var dial = Descendants<RotaryValueControl>(view).Single(control =>
            AutomationProperties.GetName(Descendants<Slider>(control).Single()) == "Level");
        var entry = Descendants<TextBox>(dial).Single();
        string Graph() => JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        string original = Graph();
        long originalPresentations = mixer.ConfigurationPresentationCount;
        int originalHistory = mixer.EditHistory.Entries.Count;
        entry.Text = (-7.125).ToString("G", CultureInfo.CurrentCulture) + " dB";
        var tab = new KeyEventArgs(Keyboard.PrimaryDevice, new DetachedKeySource(), 0, Key.Tab)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        entry.RaiseEvent(tab);
        string committed = Graph();
        require(!tab.Handled && mixer.GraphConfig.Nodes.Single().GainDb == -7.125
            && mixer.ConfigurationPresentationCount == originalPresentations + 1
            && mixer.EditHistory.Entries.Count == originalHistory + 1 && !mixer.EditHistory.HasGestureEdit,
            "The actual node's Tab route commits one exact level edit and one completed undo step.");
        entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, entry, null)
            { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        await layout();
        require(Graph() == committed && mixer.ConfigurationPresentationCount == originalPresentations + 1
            && mixer.EditHistory.Entries.Count == originalHistory + 1,
            "Leaving the committed node entry does not create a second graph edit or undo step.");
        require(mixer.Undo() && Graph() == original && mixer.Redo() && Graph() == committed,
            "One Undo restores the complete prior graph and one Redo restores the Tab-committed value exactly.");
        await layout();
        int history = mixer.EditHistory.Entries.Count; long presentations = mixer.ConfigurationPresentationCount;
        entry.Text = "not a number";
        require(dial.HandleEntryKey(Key.Tab, ModifierKeys.Shift) && entry.Text == "not a number"
            && Graph() == committed && mixer.EditHistory.Entries.Count == history
            && mixer.ConfigurationPresentationCount == presentations,
            "Rejected reverse Tab retains the draft and leaves graph history and dispatch unchanged.");
        require(dial.HandleEntryKey(Key.Escape, ModifierKeys.None) && Graph() == committed
            && mixer.EditHistory.Entries.Count == history && AutomationProperties.GetItemStatus(entry).Length == 0,
            "Cancelling the rejected draft clears its error without creating an audio or history edit.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class DetachedKeySource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
