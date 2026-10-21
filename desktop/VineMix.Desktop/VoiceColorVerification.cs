using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached control and real workspace checks. Never captures desktop input or opens audio.</summary>
internal static class VoiceColorVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        CheckControl(require);
        var fixture = UiPreview.CreateSnapshot([]);
        var source = GraphDefaults.NewNode("microphone", 30, 35);
        var node = GraphDefaults.NewNode("voiceColor", 430, 35);
        var output = GraphDefaults.NewNode("output", 900, 35);
        fixture.Configuration.Graph = new()
        {
            Nodes = [source, node, output], Zoom = 1, PanX = 20, PanY = 20,
            Edges = [new() { Id = "voice-color-in", From = source.Id, To = node.Id },
                new() { Id = "voice-color-out", From = node.Id, To = output.Id }]
        };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(item => new GraphNodeStatus
        { Id = item.Id, Path = new() { State = "Running" }, Channels = 2 }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        var view = Descendants<GraphNodeView>(workspace).Single(item => item.Id == node.Id);
        var pad = Descendants<VoiceColorPad>(view).Single();
        var numbers = Descendants<RotaryValueControl>(view).ToDictionary(number =>
            AutomationProperties.GetName(Descendants<Slider>(number).Single()));
        var bypass = Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == "Bypass");
        require(NodeCatalog.Categories.Single(category => category.Title == "Effects").Kinds.Contains("voiceColor")
            && numbers.Keys.ToHashSet().SetEquals(["Tone", "Character"])
            && !Descendants<ComboBox>(view).Any() && Descendants<RotaryValueControl>(view).Count() == 2
            && view.FindSocket("audio", false) is not null && view.FindSocket("audio", true) is not null,
            "Voice Color appears in Effects with one XY pad, two axis dials, audio input/output and no duplicate level control.");
        require(!view.IsNodeDragSurface(pad) && GraphWorkspace.IsEditingControl(pad)
            && pad.Focusable && pad.Cursor == Cursors.Cross && pad.ToolTip is StackPanel help
            && help.Children.OfType<TextBlock>().All(text => text.Text.Length < 80),
            "The focusable Voice Color pad owns adjustment gestures and concise help, without initiating node drag or canvas keyboard shortcuts.");
        Size size = view.RenderSize; Rect well = pad.PadBounds, square = pad.TravelBounds; Point origin = new(node.X, node.Y);
        var sockets = Descendants<GraphWorkspace.Socket>(view).Select(socket => socket.TranslatePoint(new(), view)).ToArray();
        require(Math.Abs(square.Width - square.Height) < .001 && pad.TravelBounds.Width >= 300
            && pad.HandlePosition == pad.NeutralPosition && well.Contains(pad.HandleBounds),
            "The neutral Voice Color handle stays inside a square pad with at least 300 logical pixels of travel and clear space for its labels.");
        require(pad.LabelBounds.Select(label => label.Text).SequenceEqual(["Broadcast", "Warm", "Natural", "Bright"])
            && pad.LabelBounds.All(label => well.Contains(label.Bounds) && !label.Bounds.IntersectsWith(square)
                && !label.Bounds.IntersectsWith(pad.HandleBounds) && label.Bounds.Height >= 18)
            && pad.PositionDescription == "Neutral" && !pad.NeutralMarkerVisible,
            "Four readable captions sit inside the well, clear of the square grid and resting handle; no Neutral or duplicate numeric readout is displayed.");
        render(view, "voice-color-neutral.png");
        long before = mixer.ConfigurationPresentationCount;
        pad.SetPosition(-.64, .35);
        var updated = mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id);
        require(updated.VoiceColor.Tone == -.64 && updated.VoiceColor.Ambience == .35
            && mixer.ConfigurationPresentationCount == before + 1
            && new Point(updated.X, updated.Y) == origin && mixer.GraphConfig.Edges.Count == 2,
            "A pad adjustment commits both axes in one existing control edit while preserving node position and audio connections.");
        render(view, "voice-color-warm-broadcast.png");
        pad.SetPosition(1, 1); render(view, "voice-color-bright-broadcast.png");
        pad.SetPosition(-1, 0); render(view, "voice-color-warm-natural.png");
        require(numbers["Tone"].TryCommitText("37.5 %") && numbers["Character"].TryCommitText("62.3 %")
            && Math.Abs(pad.Tone - .375) < 1e-12 && Math.Abs(pad.Ambience - .623) < 1e-12,
            "Both actual inline numeric editors accept precise percentages and refresh the same pad coordinates.");
        before = mixer.ConfigurationPresentationCount;
        require(!numbers["Tone"].TryCommitText("100.1") && !numbers["Tone"].TryCommitText("NaN")
            && !numbers["Character"].TryCommitText("-1") && !numbers["Character"].TryCommitText("Infinity")
            && numbers["Tone"].TryCommitText("37.5 %") && mixer.ConfigurationPresentationCount == before,
            "Invalid, out-of-range and unchanged numeric entries do not dispatch Voice Color updates.");
        numbers["Tone"].ResetToDefault();
        require(pad.Tone == 0 && Math.Abs(pad.Ambience - .623) < 1e-12,
            "Resetting Tone's numeric field resets only the horizontal axis and preserves the broadcast character amount.");
        numbers["Character"].ResetToDefault();
        require(pad.Tone == 0 && pad.Ambience == 0,
            "Resetting both axis fields returns the same flat and natural default as the pad's reset.");
        pad.SetPosition(.7, .55); string priorBypass = Json(mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).VoiceColor);
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).Bypass
            && Json(mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).VoiceColor) == priorBypass
            && ReferenceEquals(bypass.Background, ConsoleTheme.Get("ConsoleMuteBrush"))
            && Equals(bypass.Content, "Bypass"),
            "Voice Color uses the standard red Bypass state and retains both saved axis values.");
        render(view, "voice-color-bypassed.png");
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = new ContextMenu(); workspace.PopulateNodeMenu(menu, node.Id, () => { });
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Reset node to defaults"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(pad.Tone == 0 && pad.Ambience == 0 && !mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).Bypass
            && mixer.GraphConfig.Edges.Count == 2 && !menu.IsOpen,
            "The actual Reset node to defaults context action restores neutral processing while keeping both cables.");

        pad.SetPosition(-.3, .4); before = mixer.ConfigurationPresentationCount;
        int refreshes = pad.SettingsRefreshCount;
        for (int i = 0; i < 100; i++) mixer.RefreshPreviewTelemetry();
        require(mixer.ConfigurationPresentationCount == before && pad.SettingsRefreshCount == refreshes
            && pad.Tone == -.3 && pad.Ambience == .4,
            "One hundred ordinary telemetry refreshes do not resend Voice Color settings or redraw unchanged pad state.");
        foreach (double zoom in new[] { .3, .4, .5, .6, .75, .9, 1, 1.1, 1.25, 1.5, 1.75, 2 })
        {
            mixer.Edit(config => config.Graph.Zoom = zoom); await layout();
            var currentSockets = Descendants<GraphWorkspace.Socket>(view).Select(socket => socket.TranslatePoint(new(), view)).ToArray();
            double socketDifference = currentSockets.Zip(sockets).Max(pair => (pair.First - pair.Second).Length);
            require(Math.Abs(view.ActualWidth - size.Width) < .001 && Math.Abs(view.ActualHeight - size.Height) < .001
                && pad.PadBounds == well && pad.TravelBounds == square && view.DetailsVisible && currentSockets.Length == sockets.Length && socketDifference < .001
                && Math.Abs(view.ViewScale - zoom) < 1e-9,
                $"Voice Color retains its square pad, complete controls, node dimensions and socket positions at {zoom * 100:0}% canvas zoom."
                + (Math.Abs(view.ActualWidth - size.Width) >= .001 || Math.Abs(view.ActualHeight - size.Height) >= .001
                    || pad.PadBounds != well || pad.TravelBounds != square || socketDifference >= .001 || Math.Abs(view.ViewScale - zoom) >= 1e-9
                    ? $" Actual: node {view.RenderSize}, expected {size}; pad {pad.PadBounds}, expected {well}; grid {pad.TravelBounds}, expected {square}; socket displacement {socketDifference:G}; scale {view.ViewScale:G}." : ""));
            double tone = pad.Tone, ambience = pad.Ambience;
            pad.BeginAdjustment(pad.HandlePosition);
            pad.ContinueAdjustment(pad.HandlePosition + new Vector(pad.TravelBounds.Width * .01, -pad.TravelBounds.Height * .01), false);
            pad.EndAdjustment();
            require(Math.Abs(pad.Tone - (tone + .02)) < 1e-9 && Math.Abs(pad.Ambience - (ambience + .01)) < 1e-9
                && new Point(mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).X,
                    mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).Y) == origin,
                $"Voice Color's local-coordinate gesture remains accurate and leaves the node unmoved at {zoom * 100:0}% zoom.");
        }
        mixer.Edit(config => config.Graph.Zoom = 1); await layout();
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        double previousTone = pad.Tone, previousAmbience = pad.Ambience;
        pad.BeginAdjustment(pad.HandlePosition); pad.RaiseEvent(wheel); await layout();
        require(wheel.Handled && view.ViewScale < 1 && !pad.IsAdjusting && pad.Tone == previousTone && pad.Ambience == previousAmbience,
            "The real routed wheel event zooms the canvas over the pad without changing its parameters, and ends a held drag to prevent a coordinate jump.");
        foreach (var number in numbers.Values)
        {
            var entry = Descendants<TextBox>(number).Single();
            require(workspace.TryZoomFromWheel(new(100, 100), 120, entry, entry),
                "Voice Color's exact numeric field preserves canvas wheel zoom.");
        }
        mixer.Edit(config => config.Graph.Zoom = 1); await layout();
        require(view.RenderSize == size && PresentationSource.FromVisual(workspace) is null && mixer.IsPreview,
            "All Voice Color interaction and render checks remain detached from the user's desktop, audio and stored graph.");
    }

    private static void CheckControl(Action<bool, string> require)
    {
        var pad = new VoiceColorPad { Width = 330 };
        pad.Measure(new Size(330, pad.Height)); pad.Arrange(new Rect(0, 0, 330, pad.Height)); pad.UpdateLayout();
        int edits = 0; pad.PositionEdited += (_, _) => edits++;
        pad.Apply(0, 0, false);
        require(pad.Tone == 0 && pad.Ambience == 0 && edits == 0
            && AutomationProperties.GetItemStatus(pad).Contains("Tone 0")
            && AutomationProperties.GetHelpText(pad).Contains("Character")
            && AutomationProperties.GetHelpText(pad).Contains("broadcast"),
            "Voice Color's initial neutral position is accessible and construction sends no edits.");
        require(pad.TravelBounds.Width >= 300 && pad.TravelBounds.Width == pad.TravelBounds.Height,
            "The compact Voice Color control reserves a square 300-pixel parameter travel area inside its visual well.");
        foreach (var position in new[] { (-1d, 0d), (0d, 0d), (1d, 0d), (-1d, 1d), (0d, 1d), (1d, 1d) })
        {
            pad.SetPosition(position.Item1, position.Item2);
            var labels = pad.LabelBounds;
            require(pad.PadBounds.Contains(pad.HandleBounds)
                && labels.All(label => pad.PadBounds.Contains(label.Bounds)
                    && !label.Bounds.IntersectsWith(pad.TravelBounds) && !label.Bounds.IntersectsWith(pad.HandleBounds)
                    && new Rect(pad.RenderSize).Contains(label.Bounds))
                && labels.SelectMany((label, index) => labels.Skip(index + 1).Select(other => (label, other)))
                    .All(pair => !pair.label.Bounds.IntersectsWith(pair.other.Bounds)),
                $"At tone {position.Item1} and character {position.Item2}, the entire handle and all four captions remain inside the well without overlap.");
            require(pad.NeutralMarkerVisible == (position != (0d, 0d)),
                "The separate neutral diamond appears only when the handle is safely away from it.");
        }
        pad.SetPosition(-.64, .35);
        require(pad.PositionDescription == "Warm 64% · Broadcast 35%"
            && AutomationProperties.GetItemStatus(pad).Contains("character 35"),
            "Accessible status describes the precise Warm/Bright and Natural/Broadcast values without duplicating the numeric fields on the pad.");
        int outsideEdits = edits;
        foreach (var label in pad.LabelBounds)
        {
            Point point = new(label.Bounds.Left + label.Bounds.Width / 2, label.Bounds.Top + label.Bounds.Height / 2);
            require(!pad.ActivateAt(point, 1) && !pad.ActivateAt(point, 2) && !pad.IsAdjusting && edits == outsideEdits,
                "Clicking or double-clicking an internal caption never edits or resets the voice.");
        }
        pad.ResetToNeutral();
        Point pickup = pad.HandlePosition + new Vector(7, -4); outsideEdits = edits;
        require(pad.BeginAdjustment(pickup) && edits == outsideEdits,
            "Picking up the off-centre part of the handle preserves the grab offset without jumping.");
        pad.ContinueAdjustment(pickup + new Vector(pad.TravelBounds.Width * .1, -pad.TravelBounds.Height * .2), false);
        require(Math.Abs(pad.Tone - .2) < 1e-12 && Math.Abs(pad.Ambience - .2) < 1e-12,
            "An off-centre handle grab keeps both coordinates accurate during relative drag.");
        pad.EndAdjustment();
        foreach (var corner in new[] { (pad.TravelBounds.TopLeft, -1d, 1d), (pad.TravelBounds.TopRight, 1d, 1d),
            (pad.TravelBounds.BottomLeft, -1d, 0d), (pad.TravelBounds.BottomRight, 1d, 0d) })
        {
            require(pad.BeginAdjustment(corner.Item1) && pad.Tone == corner.Item2 && pad.Ambience == corner.Item3,
                "Clicking each active grid corner reaches its exact full-range values without moving the handle into a caption.");
            pad.EndAdjustment();
        }
        pad.SetPosition(-4, 3);
        require(pad.Tone == -1 && pad.Ambience == 1, "XY movement clamps at Warm and Broadcast boundaries.");
        pad.SetPosition(4, -3);
        require(pad.Tone == 1 && pad.Ambience == 0, "XY movement clamps at Bright and Natural boundaries.");
        int before = edits;
        require(!pad.SetPosition(double.NaN, .5) && !pad.SetPosition(.2, double.PositiveInfinity) && edits == before,
            "Non-finite pad coordinates cannot reach the control-update pipeline.");
        pad.ResetToNeutral();
        require(pad.AdjustKey(Key.Left, ModifierKeys.None) && pad.AdjustKey(Key.Up, ModifierKeys.Shift)
            && pad.Tone == -.01 && pad.Ambience == .001,
            "Arrow keys move one percentage point; Shift moves one tenth, independently on each axis.");
        pad.AdjustKey(Key.Home, ModifierKeys.None); before = edits;
        require(pad.AdjustKey(Key.Home, ModifierKeys.None) && edits == before && pad.Tone == 0 && pad.Ambience == 0,
            "Home resets the whole pad to neutral and a repeated reset sends no redundant edit.");
        pad.SetPosition(.4, .2); pad.BeginAdjustment(pad.HandlePosition);
        Point start = pad.HandlePosition;
        pad.ContinueAdjustment(start, true); before = edits;
        pad.ContinueAdjustment(start, false);
        require(edits == before && pad.Tone == .4 && pad.Ambience == .2,
            "Changing Shift without pointer motion never causes a tone or character jump.");
        pad.ContinueAdjustment(start + new Vector(pad.TravelBounds.Width / 2, -pad.TravelBounds.Height), true);
        require(Math.Abs(pad.Tone - .5) < 1e-12 && Math.Abs(pad.Ambience - .3) < 1e-12,
            "Fine dragging uses one tenth of the normal pad movement on both axes.");
        pad.Apply(pad.Tone, pad.Ambience, true);
        require(pad.IsAdjusting, "Synchronous model refresh and bypass presentation retain an ongoing pad gesture.");
        pad.ActivateAt(pad.TravelBounds.TopLeft, 2);
        require(pad.Tone == 0 && pad.Ambience == 0 && !pad.IsAdjusting,
            "The same double-click action used by pointer input resets both axes and cancels a pending drag.");
        pad.BeginAdjustment(pad.HandlePosition); pad.IsEnabled = false;
        before = edits; pad.ContinueAdjustment(new(1000, -1000), false);
        require(!pad.IsAdjusting && !pad.SetPosition(1, 1) && !pad.AdjustKey(Key.Up, ModifierKeys.None) && edits == before,
            "Engine-unavailable disabling cancels a pad gesture and prevents all parameter edits.");
        pad.IsEnabled = true; pad.BeginAdjustment(pad.HandlePosition);
        pad.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        require(!pad.IsAdjusting, "Unloading a Voice Color node releases its pending adjustment.");
        for (int index = 0; index < 1000; index++)
        {
            pad.SetPosition(Math.Sin(index * .071) * 2, Math.Cos(index * .13));
            pad.Apply(pad.Tone, pad.Ambience, false);
        }
        require(double.IsFinite(pad.Tone) && pad.Tone is >= -1 and <= 1 && pad.Ambience is >= 0 and <= 1,
            "One thousand rapid pad movements and synchronous refreshes remain finite and within axis bounds.");
    }

    private static string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T found) yield return found;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
