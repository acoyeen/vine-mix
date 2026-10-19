using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached controls exercise real node-to-model gesture wiring without desktop input or audio.</summary>
internal static class GestureHistoryVerification
{
    internal static void Run(Action<bool, string> require)
    {
        ConsoleTheme.Initialize();
        foreach (string kind in new[] { "mix", "tone", "compressor", "eq" }) CheckRotary(kind, require);
        CheckVoiceColor(require);
        CheckEqualizer(require);
        CheckEqualizerGroup(require);
        CheckEqualizerGroupDials(require);
        CheckInterruptions(require);
    }

    private static void CheckRotary(string kind, Action<bool, string> require)
    {
        using var fixture = new Fixture(kind);
        var dial = Descendants<RotaryValueControl>(fixture.View).First();
        var range = Descendants<Slider>(dial).Single();
        string before = fixture.Graph;
        dial.BeginAdjustment(0);
        for (int sample = 1; sample <= 120; sample++) dial.ContinueAdjustment(-sample, false);
        string after = fixture.Graph;
        require(after != before && fixture.Mixer.EditHistory.Entries.Count == 0 && fixture.Mixer.CanUndo,
            $"{kind}: all 120 pointer samples remain one pending undo gesture until capture ends.");
        LoseCapture(range); LoseCapture(range);
        VerifyRoundTrip(fixture, before, after, kind + " dial capture loss", require);

        int count = fixture.Mixer.EditHistory.Entries.Count;
        require(dial.TryCommitText(dial.DefaultValue.ToString(CultureInfo.CurrentCulture))
            && fixture.Mixer.EditHistory.Entries.Count == count + 1,
            $"{kind}: exact numeric entry after a drag is a separate undo step.");
        dial.AdjustWithKey(Key.Right);
        dial.ResetToDefault();
        require(fixture.Mixer.EditHistory.Entries.Count == count + 3,
            $"{kind}: keyboard adjustment and reset remain distinct from the preceding typed edit.");
        count = fixture.Mixer.EditHistory.Entries.Count;
        dial.BeginAdjustment(0); LoseCapture(range);
        require(fixture.Mixer.EditHistory.Entries.Count == count,
            $"{kind}: a click without value movement creates no undo step.");
    }

    private static void CheckVoiceColor(Action<bool, string> require)
    {
        using var fixture = new Fixture("voiceColor");
        var pad = Descendants<VoiceColorPad>(fixture.View).Single();
        string before = fixture.Graph;
        Point start = new(pad.TravelBounds.Left + pad.TravelBounds.Width * .65,
            pad.TravelBounds.Bottom - pad.TravelBounds.Height * .2);
        require(pad.BeginAdjustment(start), "Voice Color starts an inert pointer gesture inside the pad.");
        for (int sample = 1; sample <= 100; sample++)
            pad.ContinueAdjustment(start + new Vector(sample * .2, -sample * .1), false);
        string after = fixture.Graph;
        require(after != before && fixture.Mixer.EditHistory.Entries.Count == 0,
            "Voice Color includes the initial click-to-position change and both axes in the same pending gesture.");
        LoseCapture(pad); LoseCapture(pad);
        VerifyRoundTrip(fixture, before, after, "Voice Color capture loss", require);
        string stopped = fixture.Graph;
        pad.ContinueAdjustment(start + new Vector(80, -80), false);
        require(fixture.Graph == stopped, "Voice Color does not continue editing after capture loss.");
        pad.ResetToNeutral();
        require(fixture.Mixer.EditHistory.Entries.Count == 2,
            "Voice Color reset is a discrete edit after its completed gesture.");
    }

    private static void CheckEqualizer(Action<bool, string> require)
    {
        using var fixture = new Fixture("eq");
        var editor = Descendants<EqualizerEditor>(fixture.View).Single();
        string before = fixture.Graph;
        var band = editor.SnapshotBands()[0];
        Point start = EqualizerEditor.BandToPoint(band.FrequencyHz, band.GainDb, editor.PlotBounds);
        require(editor.BeginAdjustment(0, start), "EQ starts an inert band-adjustment gesture.");
        for (int sample = 1; sample <= 100; sample++)
            editor.ContinueAdjustment(start + new Vector(sample * .2, -sample * .1));
        string after = fixture.Graph;
        require(after != before && fixture.Mixer.EditHistory.Entries.Count == 0,
            "EQ frequency and gain samples stay inside one pending gesture.");
        editor.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        editor.EndAdjustment();
        VerifyRoundTrip(fixture, before, after, "EQ unload", require);
        require(!editor.ContinueAdjustment(start + new Vector(80, -80)) && fixture.Graph == after,
            "An unloaded EQ cannot continue its old pointer gesture.");
        editor.ResetBandPosition(0);
        require(fixture.Mixer.EditHistory.Entries.Count == 2,
            "Resetting an EQ band remains separate from its completed drag.");
    }

    private static void CheckEqualizerGroup(Action<bool, string> require)
    {
        using var fixture = new Fixture("eq");
        var editor = Descendants<EqualizerEditor>(fixture.View).Single();
        // Refresh this real node view just as the workspace does after its model
        // accepts each sample. This deliberately re-enters Apply synchronously.
        int edits = 0;
        editor.BandsEdited += _ =>
        {
            edits++;
            fixture.View.Apply(fixture.Mixer.GraphConfig.Nodes[0], null, false);
        };
        string before = fixture.Graph;
        editor.SelectBand(1); editor.SelectBand(3, toggle: true);
        editor.SelectAllBands(); editor.SelectBand(1); editor.SelectBand(3, toggle: true);
        Point empty = new(editor.PlotBounds.Left + 1, editor.PlotBounds.Top + 1);
        editor.BeginBandSelection(empty); editor.ContinueBandSelection(empty + new Vector(3, 3));
        editor.EndBandSelection(cancel: true);
        require(fixture.Graph == before && fixture.Mixer.EditHistory.Entries.Count == 0 && !fixture.Mixer.CanUndo
            && edits == 0 && editor.SelectedBandIndices.SequenceEqual(new[] { 1, 3 }),
            "EQ group selection, select-all and cancelled box selection leave the actual node model and undo history untouched.");
        var original = editor.SnapshotBands();
        Point start = EqualizerEditor.BandToPoint(original[1].FrequencyHz, original[1].GainDb, editor.PlotBounds);
        require(editor.BeginAdjustment(1, start), "EQ starts one real node-to-model group gesture.");
        for (int sample = 1; sample <= 100; sample++)
            editor.ContinueAdjustment(start + new Vector(sample * .2, -sample * .1));
        string after = fixture.Graph;
        var moved = fixture.Mixer.GraphConfig.Nodes[0].EqBands;
        require(after != before && fixture.Mixer.EditHistory.Entries.Count == 0 && fixture.Mixer.CanUndo && edits == 100
            && editor.SelectedBandIndices.SequenceEqual(new[] { 1, 3 })
            && moved[1].FrequencyHz > original[1].FrequencyHz && moved[3].FrequencyHz > original[3].FrequencyHz
            && Math.Abs(moved[1].FrequencyHz / original[1].FrequencyHz - moved[3].FrequencyHz / original[3].FrequencyHz) < .00000001
            && Math.Abs(moved[1].GainDb - original[1].GainDb - (moved[3].GainDb - original[3].GainDb)) < .00000001
            && Enumerable.Range(0, moved.Count).Where(index => index is not 1 and not 3).All(index =>
                JsonSerializer.Serialize(moved[index], Protocol.Json) == JsonSerializer.Serialize(original[index], Protocol.Json)),
            "All 100 EQ group samples update both selected model bands together, retain selection through owner refresh and remain one pending undo gesture.");
        LoseCapture(editor); LoseCapture(editor); editor.EndAdjustment(); editor.EndAdjustment();
        VerifyRoundTrip(fixture, before, after, "EQ group capture loss", require);
        require(!editor.ContinueAdjustment(start + new Vector(80, -80)) && fixture.Graph == after,
            "Capture loss ends the complete EQ group gesture and later pointer samples cannot change its model or history.");

        fixture.View.Apply(fixture.Mixer.GraphConfig.Nodes[0], null, false);
        editor.SelectBand(1); editor.SelectBand(3, toggle: true);
        int previousEntries = fixture.Mixer.EditHistory.Entries.Count, previousEdits = edits;
        string beforeDelete = fixture.Graph;
        var kept = fixture.Mixer.GraphConfig.Nodes[0].EqBands.Where((_, index) => index is not 1 and not 3)
            .Select(band => JsonSerializer.Serialize(band, Protocol.Json)).ToArray();
        editor.AdjustWithKey(Key.Delete);
        string deleted = fixture.Graph;
        require(deleted != beforeDelete && edits == previousEdits + 1
            && fixture.Mixer.EditHistory.Entries.Count == previousEntries + 1
            && fixture.Mixer.GraphConfig.Nodes[0].EqBands.Select(band => JsonSerializer.Serialize(band, Protocol.Json)).SequenceEqual(kept),
            "Deleting a selected EQ group publishes one model edit and one discrete history entry after its completed group drag.");
        require(fixture.Mixer.Undo() && fixture.Graph == beforeDelete && fixture.Mixer.Redo() && fixture.Graph == deleted,
            "One Undo restores the entire deleted EQ group and one Redo removes that same group together.");
    }

    private static void CheckEqualizerGroupDials(Action<bool, string> require)
    {
        using var fixture = new Fixture("eq");
        var editor = Descendants<EqualizerEditor>(fixture.View).Single();
        var dials = Descendants<RotaryValueControl>(fixture.View).ToArray();
        // Real workspace refreshes re-enter the node view after dial edits and
        // history restoration, not only after edits published by the plot.
        fixture.Mixer.GraphChanged += (_, _) => fixture.View.Apply(fixture.Mixer.GraphConfig.Nodes[0], null, false);
        editor.SelectBand(1); editor.SelectBand(3, toggle: true);
        var original = editor.SnapshotBands();
        string before = fixture.Graph;
        require(dials.Length == 3 && dials[0].Value == original[3].FrequencyHz
            && dials[1].Value == original[3].GainDb && dials[2].Value == original[3].Q,
            "The EQ dials show the last selected primary band's frequency, gain and width when several bands are selected.");
        require(dials[0].TryCommitText("1400") && dials[1].TryCommitText("6")
            && dials[2].TryCommitText(2.5.ToString(CultureInfo.CurrentCulture)),
            "The actual EQ numeric-entry callbacks accept exact values while a band group remains selected.");
        string after = fixture.Graph;
        var edited = fixture.Mixer.GraphConfig.Nodes[0].EqBands;
        require(edited[3].FrequencyHz == 1400 && edited[3].GainDb == 6 && edited[3].Q == 2.5
            && edited[3].ResetFrequencyHz == original[3].ResetFrequencyHz
            && Enumerable.Range(0, edited.Count).Where(index => index != 3).All(index =>
                JsonSerializer.Serialize(edited[index], Protocol.Json) == JsonSerializer.Serialize(original[index], Protocol.Json))
            && editor.SelectedBandIndex == 3 && editor.SelectedBandIndices.SequenceEqual(new[] { 1, 3 })
            && fixture.Mixer.EditHistory.Entries.Count == 3,
            "Each EQ dial edits only the primary model band, preserving the rest of its selected group and recording one history entry per exact value.");
        require(fixture.Mixer.Undo() && fixture.Mixer.Undo() && fixture.Mixer.Undo() && fixture.Graph == before
            && editor.SelectedBandIndices.SequenceEqual(new[] { 1, 3 }),
            "Undoing the three primary-band dial edits restores every original EQ setting while retaining the local group selection.");
        require(fixture.Mixer.Redo() && fixture.Mixer.Redo() && fixture.Mixer.Redo() && fixture.Graph == after
            && editor.SelectedBandIndices.SequenceEqual(new[] { 1, 3 }),
            "Redo restores all three exact primary-band values without applying them to the rest of the selected group.");
        int entries = fixture.Mixer.EditHistory.Entries.Count;
        Point start = EqualizerEditor.BandToPoint(original[1].FrequencyHz, original[1].GainDb, editor.PlotBounds);
        editor.BeginAdjustment(1, start); editor.EndAdjustment();
        require(editor.SelectedBandIndex == 1 && editor.SelectedBandIndices.SequenceEqual(new[] { 1, 3 })
            && dials[0].Value == original[1].FrequencyHz && dials[1].Value == original[1].GainDb
            && dials[2].Value == original[1].Q && fixture.Graph == after
            && fixture.Mixer.EditHistory.Entries.Count == entries,
            "Choosing another member as primary refreshes all EQ dials without moving the group, editing audio settings or adding history.");
    }

    private static void CheckInterruptions(Action<bool, string> require)
    {
        foreach (string interruption in new[] { "unload", "disable", "focus loss" })
        {
            using var fixture = new Fixture("mix");
            var dial = Descendants<RotaryValueControl>(fixture.View).Single();
            var range = Descendants<Slider>(dial).Single();
            string before = fixture.Graph;
            dial.BeginAdjustment(0); dial.ContinueAdjustment(-30, false);
            string after = fixture.Graph;
            if (interruption == "unload") dial.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            else if (interruption == "disable") dial.IsEnabled = false;
            else range.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, range, null)
                { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
            LoseCapture(range);
            VerifyRoundTrip(fixture, before, after, "Rotary " + interruption, require);
            dial.IsEnabled = true;
            dial.ContinueAdjustment(-60, false);
            require(fixture.Graph == after, $"Rotary {interruption} stops later motion from editing or extending history.");
        }
        foreach (string kind in new[] { "voiceColor", "eq" })
        foreach (string interruption in new[] { "capture loss", "unload", "disable" })
        {
            using var fixture = new Fixture(kind);
            string before = fixture.Graph;
            FrameworkElement control;
            if (kind == "voiceColor")
            {
                var pad = Descendants<VoiceColorPad>(fixture.View).Single(); control = pad;
                Point start = pad.HandlePosition; pad.BeginAdjustment(start);
                pad.ContinueAdjustment(start + new Vector(20, -20), false);
            }
            else
            {
                var editor = Descendants<EqualizerEditor>(fixture.View).Single(); control = editor;
                editor.BeginAdjustment(0, new Point(100, 100)); editor.ContinueAdjustment(new Point(120, 80));
            }
            string after = fixture.Graph;
            if (interruption == "disable") control.IsEnabled = false;
            else if (interruption == "capture loss") LoseCapture(control);
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            LoseCapture(control);
            VerifyRoundTrip(fixture, before, after, kind + " " + interruption, require);
        }
    }

    private static void VerifyRoundTrip(Fixture fixture, string before, string after, string name,
        Action<bool, string> require)
    {
        require(before != after && fixture.Mixer.EditHistory.Entries.Count == 1,
            name + " completes exactly one undo step, including repeated end notifications.");
        require(fixture.Mixer.Undo() && fixture.Graph == before && !fixture.Mixer.CanUndo,
            name + " restores the complete pre-gesture graph with one Undo.");
        require(fixture.Mixer.Redo() && fixture.Graph == after && !fixture.Mixer.CanRedo,
            name + " restores the final graph with one Redo.");
    }

    private static void LoseCapture(UIElement control) => control.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        { RoutedEvent = Mouse.LostMouseCaptureEvent });

    private sealed class Fixture : IDisposable
    {
        public MixerViewModel Mixer { get; } = new(preview: true);
        public GraphNodeView View { get; }
        public string Graph => JsonSerializer.Serialize(Mixer.GraphConfig, Protocol.Json);
        public Fixture(string kind)
        {
            var snapshot = UiPreview.CreateSnapshot([]);
            var node = GraphDefaults.NewNode(kind, 20, 20);
            snapshot.Configuration.Graph = new GraphConfiguration { Nodes = [node] };
            snapshot.Status.Nodes = [new GraphNodeStatus { Id = node.Id, Path = new() { State = "Running" }, Channels = 2 }];
            Mixer.ApplyPreviewSnapshot(snapshot);
            var workspace = new GraphWorkspace(Mixer);
            View = new GraphNodeView(workspace, Mixer.GraphConfig.Nodes[0]);
            View.Apply(Mixer.GraphConfig.Nodes[0], snapshot.Status.Nodes[0], false);
            View.Measure(new Size(View.Width, double.PositiveInfinity));
            View.Arrange(new Rect(0, 0, View.Width, View.DesiredSize.Height)); View.UpdateLayout();
        }
        public void Dispose() => Mixer.Dispose();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
