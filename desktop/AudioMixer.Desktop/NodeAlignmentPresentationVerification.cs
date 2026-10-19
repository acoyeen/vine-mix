using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached alignment actions and history checks; no desktop input, clipboard, windows or engine access.</summary>
internal static class NodeAlignmentPresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<string>? render = null)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Alignment presentation checks use only a detached preview workspace.");
        var fixture = UiPreview.CreateSnapshot([]);
        var input = GraphDefaults.NewNode("microphone", -480, -260); input.Id = "align-input";
        input.Device = new() { Id = "preview-mic1", Name = "Studio microphone" }; input.GainDb = -5.5;
        var eq = GraphDefaults.NewNode("eq", 380, 200); eq.Id = "align-eq";
        eq.EqBands[1].GainDb = 3; eq.GainDb = -2;
        var control = GraphDefaults.NewNode("control", -80, 620); control.Id = "align-control"; control.GainDb = -1;
        var output = GraphDefaults.NewNode("output", -1200, -900); output.Id = "align-output";
        output.Device = new() { Id = "preview-a1", Name = "Monitor headphones" };
        fixture.Configuration.Graph = new()
        {
            Nodes = [input, eq, control, output],
            Edges = [new() { Id = "align-in", From = input.Id, To = eq.Id, GainDb = -3 },
                new() { Id = "align-out", From = eq.Id, To = output.Id, GainDb = -2, Muted = true },
                new() { Id = "align-control-link", From = control.Id, To = eq.Id, Kind = "control", GainDb = -1 }],
            PanX = 430, PanY = 220, Zoom = .5
        };
        string[] selected = [input.Id, eq.Id, control.Id];
        string Json(object value) => JsonSerializer.Serialize(value, Protocol.Json);
        string Graph() => Json(mixer.GraphConfig);
        string Sound()
        {
            var graph = mixer.GraphConfig.Copy();
            foreach (var node in graph.Nodes) { node.X = 0; node.Y = 0; }
            return Json(new { graph.Nodes, graph.Edges });
        }
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        Rect Bounds(string id)
        {
            var view = View(id);
            return view.TransformToAncestor((Visual)view.Parent).TransformBounds(new Rect(view.RenderSize));
        }
        void RequireSeparated(IReadOnlyList<string> ids, NodeAlignment alignment)
        {
            var bounds = ids.Select(Bounds).ToArray();
            bool left = alignment == NodeAlignment.Left;
            require(bounds.All(rect => Math.Abs((left ? rect.Left : rect.Top) - (left ? bounds[0].Left : bounds[0].Top)) < .00001),
                alignment + " alignment gives every selected rendered node the same " + (left ? "left" : "top") + " edge.");
            for (int i = 1; i < bounds.Length; i++)
            {
                double gap = left ? bounds[i].Top - bounds[i - 1].Bottom : bounds[i].Left - bounds[i - 1].Right;
                require(Math.Abs(gap - 48) < .00001,
                    alignment + " keeps " + ids[i - 1] + " before " + ids[i] + " with "
                    + "exactly 48 world DIPs between their actual rendered bounds (gap " + gap + ").");
            }
        }
        async Task RenderAligned(NodeAlignment alignment, string name)
        {
            // Framing is an explicit inspection step after camera/history assertions,
            // never part of the alignment action under test.
            workspace.FrameSelection(); await layout();
            var world = (Canvas)View(input.Id).Parent;
            var viewport = (FrameworkElement)VisualTreeHelper.GetParent(world);
            require(selected.All(id =>
            {
                var view = View(id);
                var bounds = view.TransformToAncestor(viewport).TransformBounds(new Rect(view.RenderSize));
                return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= viewport.ActualWidth + 1
                    && bounds.Bottom <= viewport.ActualHeight + 1;
            }), "The " + alignment + " inspection render includes every selected node after an explicit frame command.");
            render?.Invoke(name);
        }
        MenuItem Child(MenuItem parent, string title) => parent.Items.OfType<MenuItem>().Single(item => Equals(item.Header, title));
        void SelectGroup()
        {
            workspace.SelectNode(input.Id);
            workspace.SelectNodeForPointer(eq.Id, ModifierKeys.Shift);
            workspace.SelectNodeForPointer(control.Id, ModifierKeys.Shift);
        }
        async Task Reset()
        {
            workspace.CancelActiveGesture(); mixer.ApplyPreviewSnapshot(fixture); await layout(); SelectGroup(); await layout();
            workspace.FrameSelection();
        }
        void RequireWires()
        {
            foreach (var edge in mixer.GraphConfig.Edges)
            {
                var paths = workspace.ConnectionPaths(edge.Id);
                require(paths is not null && paths.Value.Visible.Data is not null,
                    "Alignment retains the rendered " + edge.Kind + " cable " + edge.Id + ".");
                var geometry = PathGeometry.CreateFromGeometry(paths!.Value.Visible.Data);
                require(geometry.Figures.Count == 1 && geometry.Figures[0].Segments.Count == 1,
                    "The rendered cable retains its single cubic curve: " + edge.Id + ".");
                var figure = geometry.Figures[0];
                // Read actual cubic endpoints directly, independent of fill or
                // path-distance sampling behavior.
                Point from = figure.StartPoint;
                Point to = figure.Segments[0] switch
                {
                    BezierSegment curve => curve.Point3,
                    PolyBezierSegment curve when curve.Points.Count == 3 => curve.Points[2],
                    _ => throw new InvalidOperationException("The rendered cable is not a single cubic curve: " + edge.Id)
                };
                Point expectedFrom = workspace.SocketPosition(View(edge.From).FindSocket(edge.Kind, true)!);
                Point expectedTo = workspace.SocketPosition(View(edge.To).FindSocket(edge.Kind, false)!);
                require((from - expectedFrom).Length < .00001 && (to - expectedTo).Length < .00001,
                    "The visible " + edge.Kind + " cable follows its moved source and destination sockets without a manual wire refresh.");
            }
        }
        try
        {
            await Reset();
            require(selected.Select(id => View(id).ActualWidth).Distinct().Count() >= 2
                && selected.Select(id => View(id).ActualHeight).Distinct().Count() >= 2,
                "The alignment fixture includes nodes with different rendered widths and heights at negative coordinates.");
            double normalControlHeight = View(control.Id).ActualHeight;
            workspace.SelectNode("missing-node");
            require(workspace.CreateNodeAlignmentMenuItem() is null && !workspace.AlignSelectedNodes(NodeAlignment.Left),
                "No selection exposes no alignment submenu and performs no edit.");
            workspace.SelectNode(eq.Id);
            require(workspace.CreateNodeAlignmentMenuItem() is null && !workspace.AlignSelectedNodes(NodeAlignment.Top)
                && mixer.EditHistory.Entries.Count == 0,
                "One selected node has no alignment submenu or undoable alignment action.");

            foreach (NodeAlignment alignment in new[] { NodeAlignment.Left, NodeAlignment.Top })
            {
                await Reset();
                var world = (Canvas)View(input.Id).Parent;
                Matrix camera = ((MatrixTransform)world.RenderTransform).Matrix;
                string original = Graph(), sound = Sound();
                long presentations = mixer.ConfigurationPresentationCount;
                int history = mixer.EditHistory.Entries.Count;
                var submenu = workspace.CreateNodeAlignmentMenuItem()!;
                var menu = new ContextMenu(); menu.Items.Add(submenu);
                require(Equals(submenu.Header, "Align selected nodes") && submenu.IsEnabled
                    && submenu.Items.OfType<MenuItem>().Select(item => item.Header).SequenceEqual(new object[] { "Left edges", "Top edges", "Auto Align" })
                    && Child(submenu, "Left edges").IsEnabled && Child(submenu, "Top edges").IsEnabled,
                    "An idle multi-selection exposes both explicit alignment choices and Auto Align.");
                string title = alignment == NodeAlignment.Left ? "Left edges" : "Top edges";
                Child(submenu, title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await layout();
                string aligned = Graph();
                require(mixer.ConfigurationPresentationCount == presentations + 1
                    && mixer.EditHistory.Entries.Count == history + 1 && !mixer.EditHistory.HasGestureEdit,
                    title + " commits one complete configuration edit and one undo step for the entire group.");
                foreach (var before in fixture.Configuration.Graph.Nodes)
                {
                    var after = mixer.GraphConfig.Nodes.Single(node => node.Id == before.Id);
                    bool chosen = selected.Contains(before.Id);
                    bool position = chosen ? alignment == NodeAlignment.Left
                        ? after.X == input.X && after.Y >= input.Y
                        : after.Y == input.Y && after.X >= input.X
                        : after.X == before.X && after.Y == before.Y;
                    require(position && Canvas.GetLeft(View(after.Id)) == after.X && Canvas.GetTop(View(after.Id)) == after.Y,
                        title + " aligns the selected group within its original minimum origin and leaves the farther-away unselected output fixed: " + before.Id + ".");
                }
                // Both input and linked control feed EQ; every selected gap
                // normalizes even when spatial order already matches dependencies.
                string[] order = [input.Id, control.Id, eq.Id];
                RequireSeparated(order, alignment);
                require(mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).X == input.X
                    && mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).Y == input.Y,
                    "The first dependency source anchors the group at its original minimum X/Y.");
                require(Sound() == sound && ((MatrixTransform)world.RenderTransform).Matrix == camera
                    && workspace.SelectedNodeIds.ToHashSet().SetEquals(selected),
                    title + " preserves every sound setting, assignment, connection, selected ID and camera transform.");
                RequireWires();
                require(mixer.Undo() && Graph() == original && !mixer.CanUndo,
                    "One Undo restores every original node position after " + title + ".");
                await layout(); RequireWires();
                require(mixer.Redo() && Graph() == aligned && !mixer.CanRedo,
                    "One Redo restores the exact aligned group after " + title + ".");
                await layout(); RequireSeparated(order, alignment); RequireWires();
                require(((MatrixTransform)world.RenderTransform).Matrix == camera
                    && workspace.SelectedNodeIds.ToHashSet().SetEquals(selected) && Sound() == sound,
                    "Undo and Redo of alignment retain selection, camera and all audio state.");
                presentations = mixer.ConfigurationPresentationCount; history = mixer.EditHistory.Entries.Count;
                var now = workspace.CreateNodeAlignmentMenuItem()!;
                require(!Child(now, title).IsEnabled && !workspace.AlignSelectedNodes(alignment),
                    "An already aligned axis is disabled and its direct action reports a no-op.");
                // A previously enabled menu can outlive the edit; its callback must recheck the current positions.
                Child(submenu, title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                require(Graph() == aligned && mixer.ConfigurationPresentationCount == presentations
                    && mixer.EditHistory.Entries.Count == history && !menu.IsOpen && PresentationSource.FromVisual(menu) is null,
                    "Repeated or stale menu activation adds no edit/history and never opens a native popup.");
            }

            // Cover coincident origins, small gaps and oversized gaps using
            // measured rectangular bounds in both axes. Graph and selection
            // order deliberately differ from dependency order.
            foreach (NodeAlignment alignment in new[] { NodeAlignment.Left, NodeAlignment.Top })
            foreach (double? initialGap in new double?[] { null, 12, 1200 })
            {
                var regression = UiPreview.CreateSnapshot([]);
                regression.Configuration.Graph = fixture.Configuration.Graph.Copy();
                var graph = regression.Configuration.Graph;
                var rowInput = graph.Nodes.Single(node => node.Id == input.Id);
                var rowControl = graph.Nodes.Single(node => node.Id == control.Id);
                var rowEq = graph.Nodes.Single(node => node.Id == eq.Id);
                var rowOutput = graph.Nodes.Single(node => node.Id == output.Id);
                graph.Nodes = [rowEq, rowInput, rowControl, rowOutput];
                if (alignment == NodeAlignment.Left)
                {
                    rowInput.Y = rowControl.Y = rowEq.Y = -160;
                    rowInput.X = -480; rowControl.X = 20; rowEq.X = 700;
                }
                else
                {
                    rowInput.X = rowControl.X = rowEq.X = -300;
                    rowInput.Y = -260; rowControl.Y = 180; rowEq.Y = 700;
                }
                if (initialGap.HasValue)
                {
                    bool left = alignment == NodeAlignment.Left;
                    if (left)
                    {
                        rowInput.Y = -260;
                        rowControl.Y = rowInput.Y + View(input.Id).ActualHeight + initialGap.Value;
                        rowEq.Y = rowControl.Y + View(control.Id).ActualHeight + initialGap.Value;
                        rowInput.X = rowControl.X = rowEq.X = -480;
                    }
                    else
                    {
                        rowInput.X = -480;
                        rowControl.X = rowInput.X + View(input.Id).ActualWidth + initialGap.Value;
                        rowEq.X = rowControl.X + View(control.Id).ActualWidth + initialGap.Value;
                        rowInput.Y = rowControl.Y = rowEq.Y = -260;
                    }
                }
                // This persisted legacy flag no longer collapses the controls.
                // Clearance must use their real full-size presentation.
                rowControl.Collapsed = true;
                mixer.ApplyPreviewSnapshot(regression); await layout(); SelectGroup(); await layout(); workspace.FrameSelection();
                require(View(control.Id).ActualHeight == normalControlHeight,
                    "A legacy Collapsed flag still presents full-height controls for measured alignment clearance.");
                string scenario = initialGap.HasValue ? "initial " + initialGap.Value + "-DIP gaps" : "coincident primary origins";
                if (initialGap.HasValue)
                {
                    string[] order = [input.Id, control.Id, eq.Id];
                    for (int i = 1; i < order.Length; i++)
                    {
                        Rect earlier = Bounds(order[i - 1]), current = Bounds(order[i]);
                        double gap = alignment == NodeAlignment.Left ? current.Top - earlier.Bottom : current.Left - earlier.Right;
                        require(Math.Abs(gap - initialGap.Value) < .00001,
                            "The " + alignment + " regression starts with actual rendered " + scenario + ".");
                    }
                }
                var world = (Canvas)View(input.Id).Parent;
                Matrix camera = ((MatrixTransform)world.RenderTransform).Matrix;
                string original = Graph(), sound = Sound();
                long presentations = mixer.ConfigurationPresentationCount;
                int history = mixer.EditHistory.Entries.Count;
                var menu = workspace.CreateNodeAlignmentMenuItem()!;
                string title = alignment == NodeAlignment.Left ? "Left edges" : "Top edges";
                Child(menu, title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await layout();
                string aligned = Graph();
                require(mixer.ConfigurationPresentationCount == presentations + 1 && mixer.EditHistory.Entries.Count == history + 1
                    && !mixer.EditHistory.HasGestureEdit && mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).X == rowInput.X
                    && mixer.GraphConfig.Nodes.Single(node => node.Id == input.Id).Y == rowInput.Y,
                    alignment + " alignment normalizes " + scenario + " with the first source fixed and all successors packed in one edit.");
                RequireSeparated([input.Id, control.Id, eq.Id], alignment);
                require(Sound() == sound && ((MatrixTransform)world.RenderTransform).Matrix == camera
                    && workspace.SelectedNodeIds.ToHashSet().SetEquals(selected)
                    && mixer.GraphConfig.Nodes.Single(node => node.Id == output.Id).X == output.X
                    && mixer.GraphConfig.Nodes.Single(node => node.Id == output.Id).Y == output.Y,
                    "Collision-free " + title + " preserves sound, routing, source bindings, camera, selection and the unselected output.");
                RequireWires();
                presentations = mixer.ConfigurationPresentationCount;
                require(!workspace.AlignSelectedNodes(alignment) && !Child(workspace.CreateNodeAlignmentMenuItem()!, title).IsEnabled
                    && Graph() == aligned && mixer.ConfigurationPresentationCount == presentations
                    && mixer.EditHistory.Entries.Count == history + 1,
                    "Repeating packed " + title + " does not creep, add another edit or create another undo step.");
                require(mixer.Undo() && Graph() == original && !mixer.CanUndo,
                    "One Undo restores " + scenario + " exactly, including every node setting.");
                await layout(); RequireWires();
                require(mixer.Redo() && Graph() == aligned && !mixer.CanRedo,
                    "One Redo restores the exact non-overlapping positions as a single action.");
                await layout(); RequireSeparated([input.Id, control.Id, eq.Id], alignment); RequireWires();
                require(((MatrixTransform)world.RenderTransform).Matrix == camera && Sound() == sound
                    && workspace.SelectedNodeIds.ToHashSet().SetEquals(selected),
                    "Undo and Redo of packed nodes retain their camera, selection and complete audio configuration.");
                if (!initialGap.HasValue)
                    await RenderAligned(alignment, alignment == NodeAlignment.Left ? "aligned-left-same-row.png" : "aligned-top-same-column.png");
            }

            await Reset();
            foreach (var gesture in new (string Name, Action Begin, Func<bool> Active)[]
            {
                ("node move", () => workspace.BeginNodeMove(input.Id, new Point(100, 100)), () => workspace.IsNodeDragging),
                ("canvas pan", () => workspace.BeginCanvasPan(MouseButton.Middle, new Point(100, 100)), () => workspace.HasCanvasPanGesture),
                ("box selection", () => { workspace.BeginBoxSelection(new Point(100, 100), true); workspace.UpdateBoxSelection(new Point(140, 130)); }, () => workspace.SelectionBoxVisible),
                ("cable cut", () => workspace.BeginCableCut(new Point(100, 100)), () => workspace.HasCableCutPreview),
                ("connection preview", () => workspace.BeginConnectionPreview(View(input.Id).FindSocket("audio", true)!, new Point(100, 100), false), () => workspace.HasConnectionPreview)
            })
            {
                SelectGroup(); var stale = workspace.CreateNodeAlignmentMenuItem()!;
                gesture.Begin();
                string before = Graph(); long count = mixer.ConfigurationPresentationCount;
                require(!workspace.AlignSelectedNodes(NodeAlignment.Left) && workspace.CreateNodeAlignmentMenuItem() is { IsEnabled: false },
                    "Alignment and its submenu stay disabled during a " + gesture.Name + ".");
                Child(stale, "Top edges").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                require(Graph() == before && mixer.ConfigurationPresentationCount == count && gesture.Active(),
                    "An older alignment command preserves the active " + gesture.Name + " instead of cancelling or committing it.");
                workspace.CancelActiveGesture();
            }

            foreach (bool equalizer in new[] { false, true })
            {
                await Reset(); var stale = workspace.CreateNodeAlignmentMenuItem()!;
                var dial = Descendants<RotaryValueControl>(View(input.Id)).First();
                var range = Descendants<Slider>(dial).Single();
                var editor = Descendants<EqualizerEditor>(View(eq.Id)).Single();
                Point start = EqualizerEditor.BandToPoint(editor.SnapshotBands()[1].FrequencyHz, editor.SnapshotBands()[1].GainDb, editor.PlotBounds);
                if (equalizer) { editor.BeginAdjustment(1, start); editor.ContinueAdjustment(start + new Vector(12, -9)); }
                else { dial.BeginAdjustment(100); dial.ContinueAdjustment(76, false); }
                try
                {
                    string during = Graph(); long count = mixer.ConfigurationPresentationCount;
                    require(mixer.EditHistory.HasGestureEdit && !workspace.AlignSelectedNodes(NodeAlignment.Left)
                        && workspace.CreateNodeAlignmentMenuItem() is { IsEnabled: false },
                        "Alignment stays disabled while a real node " + (equalizer ? "EQ" : "dial") + " gesture has an in-progress edit.");
                    Child(stale, "Top edges").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    require(Graph() == during && mixer.ConfigurationPresentationCount == count && mixer.EditHistory.HasGestureEdit
                        && mixer.EditHistory.Entries.Count == 0,
                        "A stale alignment action cannot split, end or add to the active " + (equalizer ? "EQ" : "dial") + " gesture.");
                }
                finally
                {
                    if (equalizer) editor.EndAdjustment();
                    else range.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.LostMouseCaptureEvent });
                }
                require(mixer.EditHistory.Entries.Count == 1 && !mixer.EditHistory.HasGestureEdit,
                    "Ending the control gesture leaves only its original audio edit in history.");
            }

            await Reset();
            var priorMenu = workspace.CreateNodeAlignmentMenuItem()!;
            string prior = Graph(); long priorCount = mixer.ConfigurationPresentationCount;
            workspace.IsEnabled = false;
            try
            {
                require(workspace.CreateNodeAlignmentMenuItem() is { IsEnabled: false } && !workspace.AlignSelectedNodes(NodeAlignment.Top),
                    "A modal overlay's disabled canvas cannot be aligned through a retained command.");
                Child(priorMenu, "Left edges").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                require(Graph() == prior && mixer.ConfigurationPresentationCount == priorCount && mixer.EditHistory.Entries.Count == 0,
                    "A stale menu cannot bypass a disabled workspace or create a hidden layout edit.");
            }
            finally { workspace.IsEnabled = true; }
            workspace.SelectNode(input.Id); workspace.SelectNodeForPointer(eq.Id, ModifierKeys.Shift);
            priorMenu = workspace.CreateNodeAlignmentMenuItem()!;
            mixer.GraphConfig.Nodes.RemoveAll(node => node.Id == eq.Id); mixer.GraphConfig.Edges.Clear();
            string missing = Graph();
            require(workspace.CreateNodeAlignmentMenuItem() is null && !workspace.AlignSelectedNodes(NodeAlignment.Left),
                "A stale selected ID cannot turn one surviving node into an alignable group.");
            Child(priorMenu, "Top edges").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(Graph() == missing && mixer.ConfigurationPresentationCount == priorCount && mixer.EditHistory.Entries.Count == 0,
                "A retained menu revalidates disappeared nodes and never restores or moves a lone survivor.");

            using var offline = new MixerViewModel(preview: true);
            offline.GraphConfig.Nodes.AddRange(fixture.Configuration.Graph.Nodes.Select(node => node.Copy()));
            var offlineWorkspace = new GraphWorkspace(offline);
            offlineWorkspace.SelectNode(input.Id); offlineWorkspace.SelectNodeForPointer(eq.Id, ModifierKeys.Shift);
            var offlineMenu = offlineWorkspace.CreateNodeAlignmentMenuItem();
            string offlineBefore = Json(offline.GraphConfig);
            require(!offline.CanEdit && offlineMenu is { IsEnabled: false }
                && offlineMenu.Items.OfType<MenuItem>().All(item => !item.IsEnabled)
                && !offlineWorkspace.AlignSelectedNodes(NodeAlignment.Left),
                "An offline preview keeps the group action visible but disabled without requesting an engine connection.");
            Child(offlineMenu!, "Top edges").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(Json(offline.GraphConfig) == offlineBefore && offline.PendingEditCount == 0 && !offline.CanUndo
                && offline.ConfigurationPresentationCount == 0 && PresentationSource.FromVisual(offlineWorkspace) is null,
                "Even direct activation of an offline menu leaves the graph, pending work and undo history untouched.");
        }
        finally { workspace.IsEnabled = true; workspace.CancelActiveGesture(); }
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
}
