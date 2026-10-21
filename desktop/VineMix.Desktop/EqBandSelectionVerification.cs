using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached group-selection checks exercise the same semantic operations as pointer and key input.</summary>
internal static class EqBandSelectionVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        CheckSelection(require);
        CheckMovement(require);
        CheckLimits(require);
        CheckMixedShapes(require);
        CheckMarquee(require);
        CheckKeyboard(require);
        var preview = Editor(PreviewBands());
        preview.SelectBand(0); preview.SelectBand(1, toggle: true); preview.SelectBand(2, toggle: true);
        return preview;
    }

    internal static FrameworkElement MarqueePreview()
    {
        var preview = Editor(PreviewBands());
        Point first = Position(preview, 0), last = Position(preview, 2);
        preview.BeginBandSelection(new Point(first.X - 18, first.Y - 22));
        preview.ContinueBandSelection(new Point(last.X + 18, last.Y + 22));
        return preview;
    }

    private static void CheckSelection(Action<bool, string> require)
    {
        var editor = Editor(PreviewBands());
        int edits = 0, starts = 0, ends = 0;
        editor.BandsEdited += _ => edits++;
        editor.EditGestureStarted += (_, _) => starts++;
        editor.EditGestureEnded += (_, _) => ends++;
        string before = Bands(editor);
        editor.SelectBand(3); editor.SelectBand(1, toggle: true);
        require(Selected(editor, 1, 3) && editor.SelectedBandIndex == 1,
            "EQ modifier selection keeps sorted band indices and makes the last added band the primary band.");
        editor.SelectBand(1, toggle: true);
        require(Selected(editor, 3) && editor.SelectedBandIndex == 3,
            "Toggling a selected primary band removes only that band and leaves a valid remaining primary.");
        editor.SelectBand(3, toggle: true);
        require(Selected(editor) && editor.SelectedBandIndex == -1,
            "Toggling the last selected EQ band clears both selection and primary band.");
        require(!editor.SelectBand(-2) && !editor.SelectBand(editor.BandCount) && Selected(editor),
            "Invalid EQ selection indices leave the current selection unchanged.");
        editor.SelectAllBands();
        require(Selected(editor, 0, 1, 2, 3) && editor.SelectedBandIndices.Contains(editor.SelectedBandIndex),
            "Select all includes every EQ point and supplies a valid primary band.");
        editor.SelectBand(2);
        require(Selected(editor, 2) && editor.SelectedBandIndex == 2,
            "An ordinary EQ selection replaces a group with the clicked point.");
        editor.SelectBand(0, toggle: true);
        editor.Apply(editor.SnapshotBands(), false, editor.SelectedBandIndex);
        require(Selected(editor, 0, 2),
            "Refreshing owner settings with the current primary retains the complete EQ band selection.");
        require(Bands(editor) == before && edits == 0 && starts == 0 && ends == 0,
            "Selecting, toggling, clearing and refreshing EQ selection never changes audio settings or opens an edit gesture.");
    }

    private static void CheckMovement(Action<bool, string> require)
    {
        var editor = Editor([
            Band(400, 2, 1.3, 320), Band(1600, -4, 2.7, 1250), Band(6400, 8, .6, 8000)
        ]);
        var before = editor.SnapshotBands();
        int edits = 0, starts = 0, ends = 0;
        editor.BandsEdited += bands => { edits++; editor.Apply(bands, false, editor.SelectedBandIndex); };
        editor.EditGestureStarted += (_, _) => starts++;
        editor.EditGestureEnded += (_, _) => ends++;
        editor.SelectBand(0); editor.SelectBand(1, toggle: true);
        Point start = Position(editor, 0);
        require(editor.BeginAdjustment(0, start) && Selected(editor, 0, 1) && editor.SelectedBandIndex == 0,
            "Beginning a drag on any selected EQ band preserves its group and makes that band primary.");
        Point pointer = start + new Vector(editor.PlotBounds.Width * Math.Log(1.5) / Math.Log(1000),
            -editor.PlotBounds.Height * 3 / 48);
        editor.ContinueAdjustment(pointer);
        var moved = editor.SnapshotBands();
        require(Near(moved[0].FrequencyHz, 600) && Near(moved[1].FrequencyHz, 2400)
            && Near(moved[0].GainDb, 5) && Near(moved[1].GainDb, -1)
            && Same(moved[2], before[2]) && Selected(editor, 0, 1) && edits == 1,
            "A group drag applies one shared logarithmic frequency ratio and gain offset in one atomic snapshot, even through synchronous owner refresh.");
        int previousEdits = edits;
        require(!editor.ContinueAdjustment(pointer, fine: true) && edits == previousEdits,
            "Pressing Shift during an EQ group drag without new pointer movement produces no value jump or redundant edit.");
        pointer += new Vector(editor.PlotBounds.Width * Math.Log(2) / Math.Log(1000), -editor.PlotBounds.Height * 5 / 48);
        editor.ContinueAdjustment(pointer, fine: true);
        var fine = editor.SnapshotBands();
        require(Near(fine[0].FrequencyHz / moved[0].FrequencyHz, Math.Pow(2, .2))
            && Near(fine[1].FrequencyHz / moved[1].FrequencyHz, Math.Pow(2, .2))
            && Near(fine[0].GainDb - moved[0].GainDb, 1) && Near(fine[1].GainDb - moved[1].GainDb, 1),
            "Fine EQ group movement scales only new displacement equally for every selected band.");
        require(!editor.ContinueAdjustment(pointer) && editor.SnapshotBands().Select((band, index) =>
                band.Q == before[index].Q && band.ResetFrequencyHz == before[index].ResetFrequencyHz).All(value => value),
            "Releasing Shift without motion creates no jump, and dragging a group preserves every width and reset-frequency anchor.");
        editor.EndAdjustment(); editor.EndAdjustment();
        require(starts == 1 && ends == 1 && !editor.ContinueAdjustment(pointer + new Vector(5, 5)),
            "An EQ group gesture starts and ends once, and later motion cannot extend the finished edit.");
        editor.BeginAdjustment(2, Position(editor, 2)); editor.EndAdjustment();
        require(Selected(editor, 2), "Dragging an unselected EQ point begins an exclusive single-band selection.");
    }

    private static void CheckLimits(Action<bool, string> require)
    {
        var editor = Editor([Band(1000, 23), Band(10000, -4)]);
        editor.SelectAllBands();
        Point pointer = Position(editor, 0);
        editor.BeginAdjustment(0, pointer);
        pointer += new Vector(10000, -10000); editor.ContinueAdjustment(pointer);
        var upper = editor.SnapshotBands();
        require(Near(upper[0].FrequencyHz, 2000) && Near(upper[1].FrequencyHz, 20000)
            && Near(upper[0].GainDb, 24) && Near(upper[1].GainDb, -3),
            "When a selected point reaches an upper EQ limit, the entire group stops together and preserves frequency ratios and gain spacing.");
        pointer += new Vector(-1, 1); editor.ContinueAdjustment(pointer);
        var reversed = editor.SnapshotBands();
        require(reversed[0].FrequencyHz < upper[0].FrequencyHz && reversed[1].FrequencyHz < upper[1].FrequencyHz
            && reversed[0].GainDb < upper[0].GainDb && reversed[1].GainDb < upper[1].GainDb
            && Near(reversed[1].FrequencyHz / reversed[0].FrequencyHz, 10)
            && Near(reversed[0].GainDb - reversed[1].GainDb, 27),
            "Reversing an EQ group drag away from its limits responds immediately without accumulating pointer overshoot.");
        pointer += new Vector(-10000, 10000); editor.ContinueAdjustment(pointer);
        var lower = editor.SnapshotBands();
        require(Near(lower[0].FrequencyHz, 20) && Near(lower[1].FrequencyHz, 200)
            && Near(lower[0].GainDb, 3) && Near(lower[1].GainDb, -24),
            "The lower frequency and gain limits constrain the common group movement without collapsing the spacing between selected bands.");
        pointer += new Vector(1, -1); editor.ContinueAdjustment(pointer); editor.EndAdjustment();
        var returned = editor.SnapshotBands();
        require(returned[0].FrequencyHz > 20 && returned[1].FrequencyHz > 200
            && returned[0].GainDb > 3 && returned[1].GainDb > -24,
            "A group drag also responds immediately when reversed away from its lower limits.");
    }

    private static void CheckMixedShapes(Action<bool, string> require)
    {
        var bands = new[] {
            Band(1000, 2, 2, 800), Band(2000, 0, 4, 1800, "notch"),
            Band(4000, 0, .7, 3500, "highPass"), Band(8000, 0, .8, 7500, "lowPass"),
            Band(12000, -8, 1.2, 11000, "highShelf", enabled: false), Band(500, 1, .9, 450, "lowShelf")
        };
        var editor = Editor(bands);
        editor.SelectAllBands(); editor.SelectBand(5, toggle: true);
        Point start = Position(editor, 1);
        editor.BeginAdjustment(1, start);
        editor.ContinueAdjustment(start + new Vector(editor.PlotBounds.Width * Math.Log(1.25) / Math.Log(1000),
            -editor.PlotBounds.Height * 4 / 48)); editor.EndAdjustment();
        var after = editor.SnapshotBands();
        require(Enumerable.Range(0, 5).All(index => Near(after[index].FrequencyHz, bands[index].FrequencyHz * 1.25))
            && Near(after[0].GainDb, 6) && Near(after[4].GainDb, -4)
            && after.Skip(1).Take(3).All(band => band.GainDb == 0) && Same(after[5], bands[5]),
            "Mixed EQ groups move notch, saved pass filters and disabled points in frequency, while vertical movement edits only the gain-capable selected filters.");
        require(after.Select((band, index) => band.Type == bands[index].Type && band.Enabled == bands[index].Enabled
                && band.Q == bands[index].Q && band.ResetFrequencyHz == bands[index].ResetFrequencyHz).All(value => value),
            "Moving a mixed EQ group retains all filter shapes, enabled states, Q values and persistent reset anchors.");
    }

    private static void CheckMarquee(Action<bool, string> require)
    {
        var editor = Editor(PreviewBands());
        int edits = 0, starts = 0, ends = 0;
        editor.BandsEdited += _ => edits++;
        editor.EditGestureStarted += (_, _) => starts++;
        editor.EditGestureEnded += (_, _) => ends++;
        editor.SelectBand(3);
        string before = Bands(editor);
        require(!editor.BeginBandSelection(new Point(editor.PlotBounds.Left - 1, editor.PlotBounds.Top))
            && Selected(editor, 3) && editor.BandSelectionBounds.IsEmpty,
            "EQ box selection can begin only inside the plot and cannot clear a selection from a caption or axis click.");
        Point first = Position(editor, 0), second = Position(editor, 1);
        Point start = new(first.X - 14, Math.Min(first.Y, second.Y) - 14);
        Point finish = new(second.X + 14, Math.Max(first.Y, second.Y) + 14);
        editor.BeginBandSelection(start); editor.ContinueBandSelection(finish);
        require(Selected(editor, 0, 1) && !editor.BandSelectionBounds.IsEmpty
            && editor.PlotBounds.Contains(editor.BandSelectionBounds),
            "Dragging an empty EQ plot area selects every band centre inside its visible selection rectangle.");
        editor.ContinueBandSelection(new Point(editor.PlotBounds.Right + 200, editor.PlotBounds.Bottom + 200));
        require(editor.PlotBounds.Contains(editor.BandSelectionBounds),
            "The visible EQ selection rectangle remains clipped to the plot when its pointer leaves the graph.");
        editor.EndBandSelection(cancel: true); editor.EndBandSelection();
        require(Selected(editor, 3) && editor.SelectedBandIndex == 3 && editor.BandSelectionBounds.IsEmpty,
            "Cancelling EQ box selection restores the original full selection and primary, and repeated completion is inert.");
        editor.BeginBandSelection(start, additive: true); editor.ContinueBandSelection(finish); editor.EndBandSelection();
        require(Selected(editor, 0, 1, 3) && editor.BandSelectionBounds.IsEmpty,
            "Modifier box selection adds enclosed points while retaining points already selected outside its rectangle.");
        editor.BeginBandSelection(start); editor.ContinueBandSelection(finish);
        require(editor.AdjustWithKey(Key.Escape) && Selected(editor, 0, 1, 3) && editor.BandSelectionBounds.IsEmpty,
            "The actual Escape key decision cancels an EQ box selection and restores its previous group.");
        require(Bands(editor) == before && edits == 0 && starts == 0 && ends == 0,
            "EQ box selection, clipping, additive selection and cancellation produce no settings edit or audio-history gesture.");
    }

    private static void CheckKeyboard(Action<bool, string> require)
    {
        var editor = Editor([Band(400, 2, 1.3, 320), Band(1600, -4, 2.7, 1250),
            Band(6400, 8, .6, 8000), Band(10000, -3, 2, 9000)]);
        int edits = 0;
        editor.BandsEdited += bands => { edits++; editor.Apply(bands, false, editor.SelectedBandIndex); };
        editor.SelectBand(0);
        require(editor.AdjustWithKey(Key.Right, fine: true, control: true) && Selected(editor, 0, 1)
            && editor.SelectedBandIndex == 1 && edits == 0,
            "Ctrl+Shift+Right extends the EQ selection and moves its primary without editing audio settings.");
        require(editor.AdjustWithKey(Key.Left, control: true) && Selected(editor, 0)
            && editor.SelectedBandIndex == 0 && edits == 0,
            "Ctrl+Left navigates to one EQ band and replaces the group selection.");
        require(editor.AdjustWithKey(Key.A, control: true) && Selected(editor, 0, 1, 2, 3) && edits == 0,
            "The actual Ctrl+A key decision selects every EQ band without creating a settings edit.");
        editor.SelectBand(0); editor.SelectBand(1, toggle: true);
        var before = editor.SnapshotBands();
        editor.AdjustWithKey(Key.Right); editor.AdjustWithKey(Key.Up);
        editor.AdjustWithKey(Key.Left, fine: true); editor.AdjustWithKey(Key.Down, fine: true);
        var moved = editor.SnapshotBands();
        require(Enumerable.Range(0, 2).All(index => Near(moved[index].FrequencyHz / before[index].FrequencyHz,
                Math.Pow(2, 1d / 12 - 1d / 120)) && Near(moved[index].GainDb - before[index].GainDb, .4))
            && Same(moved[2], before[2]) && Same(moved[3], before[3]) && edits == 4 && Selected(editor, 0, 1),
            "Coarse and fine arrow keys move the entire selected EQ group atomically while leaving unselected points unchanged through owner refreshes.");
        editor.SelectBand(0); editor.SelectBand(2, toggle: true);
        int previousEdits = edits;
        editor.AdjustWithKey(Key.Home);
        var reset = editor.SnapshotBands();
        require(edits == previousEdits + 1 && Near(reset[0].FrequencyHz, 320) && reset[0].GainDb == 0
            && Near(reset[2].FrequencyHz, 8000) && reset[2].GainDb == 0 && reset[0].Q == before[0].Q
            && reset[2].Q == before[2].Q && Same(reset[1], moved[1]) && Same(reset[3], moved[3]),
            "Home resets every selected EQ band's anchored frequency and gain together in one edit, preserving widths and unselected settings.");
        previousEdits = edits;
        editor.AdjustWithKey(Key.Delete);
        var remaining = editor.SnapshotBands();
        require(edits == previousEdits + 1 && remaining.Count == 2 && Same(remaining[0], reset[1])
            && Same(remaining[1], reset[3]) && editor.SelectedBandIndices.All(index => index >= 0 && index < remaining.Count),
            "Delete removes all selected EQ indices in one atomic edit without removing neighbouring bands or leaving invalid selections.");
        editor.SelectAllBands(); previousEdits = edits;
        editor.AdjustWithKey(Key.Back);
        require(edits == previousEdits + 1 && editor.BandCount == 0 && Selected(editor) && editor.SelectedBandIndex == -1,
            "Backspace can remove the complete EQ group in one edit and clears the resulting empty selection.");
        require(!editor.AdjustWithKey(Key.F2), "Unrelated keys remain available to the EQ editor's parent controls.");
    }

    private static EqualizerEditor Editor(EqBand[] bands)
    {
        var editor = new EqualizerEditor { Width = 540, Height = 300 };
        editor.Apply(bands, false, -1);
        editor.Measure(new Size(editor.Width, editor.Height));
        editor.Arrange(new Rect(0, 0, editor.Width, editor.Height)); editor.UpdateLayout();
        return editor;
    }

    private static EqBand[] PreviewBands() => [Band(300, 8), Band(900, 2), Band(2600, -5), Band(10000, -12)];
    private static EqBand Band(double frequency, double gain, double q = 1, double anchor = 0, string type = "peak", bool enabled = true)
        => new() { Type = type, FrequencyHz = frequency, GainDb = gain, Q = q,
            ResetFrequencyHz = anchor > 0 ? anchor : frequency, Enabled = enabled };
    private static Point Position(EqualizerEditor editor, int index)
    {
        var band = editor.SnapshotBands()[index];
        return EqualizerEditor.BandToPoint(band.FrequencyHz, band.GainDb, editor.PlotBounds);
    }
    private static string Bands(EqualizerEditor editor) => JsonSerializer.Serialize(editor.SnapshotBands(), Protocol.Json);
    private static bool Selected(EqualizerEditor editor, params int[] expected) => editor.SelectedBandIndices.SequenceEqual(expected);
    private static bool Near(double first, double second) => Math.Abs(first - second) <= Math.Max(1, Math.Abs(second)) * .00000001;
    private static bool Same(EqBand first, EqBand second) => first.Type == second.Type && first.Enabled == second.Enabled
        && first.FrequencyHz == second.FrequencyHz && first.GainDb == second.GainDb
        && first.Q == second.Q && first.ResetFrequencyHz == second.ResetFrequencyHz;
}
