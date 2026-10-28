using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Real workspace actions on detached example nodes; never opens audio, settings or the clipboard.</summary>
internal static class NamedFrameVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer,
        Func<Task> layout, Action<bool, string> require, Action<string>? capture = null)
    {
        require(mixer.IsPreview, "Named groups are verified with an inert preview model.");
        NamedFramePersistenceVerification.Run(require);
        var fixture = UiPreview.CreateSnapshot([]);
        var source = Node("microphone", "frame-source", -500, 120);
        var gain = Node("gain", "frame-gain", 0, 120);
        var compressor = Node("compressor", "frame-compressor", 460, 136);
        var output = Node("output", "frame-output", 1060, 120);
        var outside = Node("gain", "frame-outside", 180, 760);
        fixture.Configuration.Graph = new()
        {
            Nodes = [source, gain, compressor, output, outside],
            Edges = [new() { Id = "frame-through-cable", From = source.Id, To = output.Id, GainDb = -3 }],
            Zoom = .5, PanX = 280, PanY = 80
        };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.IsLayoutLocked = false;
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        mixer.EditHistory.Clear();
        GraphConfiguration Graph() => mixer.GraphConfig;
        GraphNode Current(string id) => Graph().Nodes.Single(node => node.Id == id);
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        Rect NodeBounds(string id)
        {
            var view = View(id);
            return new(Canvas.GetLeft(view), Canvas.GetTop(view), view.ActualWidth, view.ActualHeight);
        }
        Point Screen(Point point) => new(point.X * Graph().Zoom + Graph().PanX, point.Y * Graph().Zoom + Graph().PanY);
        void Select(params string[] ids)
        {
            workspace.SelectRectangle(Screen(new(-4000, -4000)), Screen(new(-3990, -3990)), false);
            foreach (string id in ids)
            {
                Rect bounds = NodeBounds(id);
                workspace.SelectRectangle(Screen(new(bounds.Left + 2, bounds.Top + 2)),
                    Screen(new(bounds.Left + 3, bounds.Top + 3)), true);
            }
        }
        string EngineJson() => JsonSerializer.Serialize(Graph(), Protocol.Json);
        string FullGraph() => JsonSerializer.Serialize(new { Graph = Graph(), Frames = Graph().Frames }, Protocol.Json);
        string Routing() => JsonSerializer.Serialize(Graph().Edges, Protocol.Json);
        bool HasFrame(string id) => Graph().Frames.Any(frame => frame.Id == id);
        GraphFrame Frame(string id) => Graph().Frames.Single(frame => frame.Id == id);

        require(GraphDefaults.Validate(Graph()).Length == 0, "The named-group fixture starts with a valid independent audio path.");
        Select(gain.Id, compressor.Id);
        string before = EngineJson(), routing = Routing();
        int history = mixer.EditHistory.Entries.Count;
        var create = workspace.CreateGroupMenuItem();
        require(create is not null && create.IsEnabled,
            "The normal selected-node menu offers creation of an organizational group.");
        string? created = workspace.CreateFrameFromSelection();
        await layout();
        require(created is not null && HasFrame(created) && Graph().Frames.Count == 1,
            "Creating a group adds one stable frame identity to the selected nodes.");
        string id = created!;
        require(Frame(id).NodeIds.ToHashSet(StringComparer.Ordinal).SetEquals([gain.Id, compressor.Id])
            && EngineJson() == before && Routing() == routing && mixer.EditHistory.Entries.Count == history + 1,
            "Group creation records one undo step but adds no nodes, ports, engine configuration or audio connections.");
        require(mixer.Undo() && Graph().Frames.Count == 0 && EngineJson() == before
            && mixer.Redo() && HasFrame(id) && EngineJson() == before,
            "Undo and Redo remove and restore the same organizational frame without editing the engine graph.");
        await layout();
        var frameView = workspace.FrameViews[id];
        var bounds = workspace.FrameBounds(id);
        require(bounds.Contains(NodeBounds(gain.Id)) && bounds.Contains(NodeBounds(compressor.Id))
            && bounds.Top < NodeBounds(gain.Id).Top && bounds.Left < NodeBounds(gain.Id).Left
            && bounds.Right > NodeBounds(compressor.Id).Right,
            "The group automatically encloses actual member dimensions with space for its title and padding.");
        require(!Descendants<GraphWorkspace.Socket>(frameView).Any(),
            "An organizational frame contains no input or output sockets.");
        Point bodyPoint = new(20, frameView.ActualHeight - 10);
        Point headerPoint = new(frameView.ActualWidth / 2, frameView.HeaderElement.ActualHeight / 2);
        var bodyHit = DetachedHit(frameView, bodyPoint);
        var headerHit = DetachedHit(frameView, headerPoint);
        bool headerOrChild = Ancestors(headerHit).Any(parent => ReferenceEquals(parent, frameView.HeaderElement));
        bool bodyOrChild = Ancestors(bodyHit).Any(parent => ReferenceEquals(parent, frameView));
        require(bodyOrChild && headerOrChild,
            "Both the group heading and exposed body participate in pointer hit testing."
            + (bodyOrChild && headerOrChild ? "" : $" Body point={bodyPoint}, geometry={DescribeHit(bodyHit)}, input={DescribeHit(frameView.InputHitTest(bodyPoint) as DependencyObject)};"
                + $" header point={headerPoint}, geometry={DescribeHit(headerHit)}, input={DescribeHit(frameView.InputHitTest(headerPoint) as DependencyObject)};"
                + $" frame size={frameView.RenderSize}, visible={frameView.IsVisible}/{frameView.Visibility}, hit={frameView.IsHitTestVisible};"
                + $" header size={frameView.HeaderElement.RenderSize}, visible={frameView.HeaderElement.IsVisible}/{frameView.HeaderElement.Visibility}, hit={frameView.HeaderElement.IsHitTestVisible}."));
        capture?.Invoke("named-frames-created.png");

        history = mixer.EditHistory.Entries.Count;
        workspace.RenameFrame(id, "Voice preparation");
        workspace.RecolorFrame(id, "violet"); await layout();
        require(Frame(id).Name == "Voice preparation" && Frame(id).ColorKey == "violet"
            && frameView.DisplayedName == "Voice preparation" && EngineJson() == before
            && mixer.EditHistory.Entries.Count == history + 2,
            "Renaming and a preset color update the visible group with separate undo steps and no engine edit.");
        require(mixer.Undo() && Frame(id).ColorKey != "violet" && Frame(id).Name == "Voice preparation"
            && mixer.Undo() && Frame(id).Name != "Voice preparation"
            && mixer.Redo() && mixer.Redo() && Frame(id).ColorKey == "violet" && Frame(id).Name == "Voice preparation",
            "Name and color survive their independent Undo and Redo transitions.");
        await layout();
        var menu = workspace.CreateFrameMenu(id);
        var choices = MenuItems(menu).Where(item => GraphFramePalette.All.Any(color => color.Name == item.Header?.ToString())).ToArray();
        require(choices.Length == GraphFramePalette.All.Count && choices.All(item => item.IsCheckable)
            && !Descendants<Slider>(menu).Any(),
            "The group menu provides the complete curated color list as named choices rather than a color wheel.");
        require(MenuItems(menu).Any(item => item.Header?.ToString()?.Contains("Ungroup", StringComparison.OrdinalIgnoreCase) == true),
            "The group menu explicitly offers ungrouping while retaining its member nodes.");

        // Member bounds follow a drag preview, cancellation and real size changes.
        Select(compressor.Id);
        bounds = workspace.FrameBounds(id);
        Point memberStart = Screen(NodeBounds(compressor.Id).TopLeft + new Vector(50, 12));
        workspace.BeginNodeMove(compressor.Id, memberStart);
        // Keep this individual-node drop clear of the unrelated through cable:
        // existing node insertion remains available when members move alone.
        workspace.UpdateNodeMove(memberStart + new Vector(70, -400)); await layout();
        require(workspace.FrameBounds(id).Right > bounds.Right + 100
            && workspace.FrameBounds(id).Contains(NodeBounds(compressor.Id)) && EngineJson() == before,
            "Moving a member expands its frame during the live drag without prematurely committing positions.");
        mixer.RefreshPreviewTelemetry(); await layout();
        require(workspace.FrameBounds(id).Contains(NodeBounds(compressor.Id)) && workspace.FrameBounds(id).Right > bounds.Right + 100,
            "Telemetry refreshes retain a frame's live member-drag bounds.");
        workspace.FinishNodeMove(cancel: true); await layout();
        require(workspace.FrameBounds(id) == bounds && EngineJson() == before,
            "Cancelling a member drag restores both its position and its frame's original bounds.");
        workspace.BeginNodeMove(compressor.Id, memberStart);
        workspace.UpdateNodeMove(memberStart + new Vector(70, -400));
        require(workspace.InsertionPreviewId is null,
            "The individual-member bounds fixture is clear of audio insertion targets."
            + (workspace.InsertionPreviewId is null ? "" : $" Unexpected target={workspace.InsertionPreviewId}, node bounds={NodeBounds(compressor.Id)}."));
        workspace.FinishNodeMove(); await layout();
        bool memberMovePassed = workspace.FrameBounds(id).Right > bounds.Right + 100
            && workspace.FrameBounds(id).Contains(NodeBounds(compressor.Id)) && Routing() == routing;
        require(memberMovePassed,
            "Committing an individual member move retains membership and derives the new frame bounds from its final position."
            + (memberMovePassed ? "" : $" Before frame={bounds}, after frame={workspace.FrameBounds(id)}, member={NodeBounds(compressor.Id)}, zoom={Graph().Zoom}, routing unchanged={Routing() == routing}."));
        require(mixer.Undo() && EngineJson() == before,
            "Undoing an individual member move restores its saved position without removing the frame.");
        await layout();
        // Current nodes deliberately ignore their legacy Collapsed setting.
        // Change the real detached view's measured height to exercise the same
        // SizeChanged path used by future content/theme-driven size changes.
        var resizingMember = View(compressor.Id);
        double originalMinimumHeight = resizingMember.MinHeight;
        try
        {
            resizingMember.MinHeight = resizingMember.ActualHeight + 120; await layout();
            require(workspace.FrameBounds(id).Bottom > bounds.Bottom + 100
                && workspace.FrameBounds(id).Contains(NodeBounds(compressor.Id)) && EngineJson() == before,
                "A member's larger measured height automatically grows its group while leaving model settings unchanged.");
        }
        finally { resizingMember.MinHeight = originalMinimumHeight; }
        await layout();
        require(workspace.FrameBounds(id) == bounds,
            "Restoring a member's measured height shrinks its frame to the content bounds without a manual resize handle.");

        var origins = Frame(id).NodeIds.ToDictionary(nodeId => nodeId, nodeId => new Point(Current(nodeId).X, Current(nodeId).Y));
        Point untouched = new(Current(outside.Id).X, Current(outside.Id).Y);
        history = mixer.EditHistory.Entries.Count;
        before = EngineJson();
        require(!workspace.BeginFramePointerGesture(id, new(100, 100), ModifierKeys.Shift)
            && !workspace.BeginFramePointerGesture(id, new(100, 100), ModifierKeys.Control),
            "Shift and Control background presses pass through to additive canvas selection without starting a group drag.");
        require(workspace.BeginFramePointerGesture(id, new(100, 100), ModifierKeys.None)
            && workspace.SelectedNodeIds.ToHashSet().SetEquals(Frame(id).NodeIds),
            "A plain group-body press selects every member and starts the shared movement gesture.");
        workspace.UpdateNodeMove(new(127, 119)); await layout();
        require(origins.All(pair => Close(NodeBounds(pair.Key).X, pair.Value.X + 54)
            && Close(NodeBounds(pair.Key).Y, pair.Value.Y + 38))
            && workspace.FrameBounds(id).Contains(NodeBounds(gain.Id)) && EngineJson() == before,
            "Dragging the group background previews the same zoom-corrected movement for every member.");
        workspace.FinishNodeMove(); await layout();
        require(origins.All(pair => Current(pair.Key).X == pair.Value.X + 56 && Current(pair.Key).Y == pair.Value.Y + 40)
            && new Point(Current(outside.Id).X, Current(outside.Id).Y) == untouched && Routing() == routing
            && mixer.EditHistory.Entries.Count == history + 1,
            "Releasing a frame drag commits one shared grid-snapped movement and one history step while retaining routing and outside nodes.");
        string moved = EngineJson();
        require(mixer.Undo() && EngineJson() == before && HasFrame(id) && mixer.Redo() && EngineJson() == moved,
            "One Undo restores all frame members and one Redo reapplies their shared movement.");
        await layout();
        before = FullGraph(); history = mixer.EditHistory.Entries.Count;
        workspace.BeginFrameMove(id, new(100, 100)); workspace.UpdateNodeMove(new(130, 140));
        workspace.FinishNodeMove(cancel: true); await layout();
        require(FullGraph() == before && mixer.EditHistory.Entries.Count == history,
            "Cancelling a complete group drag leaves positions, metadata and history unchanged.");
        workspace.BeginFramePointerGesture(id, new(100, 100), ModifierKeys.None); workspace.FinishNodeMove();
        require(FullGraph() == before && mixer.EditHistory.Entries.Count == history,
            "Clicking a group background without moving creates no redundant undo step.");

        mixer.Edit(configuration => configuration.Graph.Zoom = .3); await layout();
        require(workspace.FrameViews[id].TitleScreenFontSize >= 15
            && workspace.FrameBounds(id).Contains(NodeBounds(gain.Id))
            && workspace.FrameBounds(id).Contains(NodeBounds(compressor.Id)),
            "At 30 percent zoom the group title remains at least 15 screen DIPs and still reserves space above every member.");
        capture?.Invoke("named-frames-overview-30-percent.png");
        // Additional visual evidence for the centered group heading across
        // the full canvas zoom range.
        foreach (double zoom in new[] { 1d, 2d })
        {
            mixer.Edit(configuration => configuration.Graph.Zoom = zoom); await layout();
            capture?.Invoke($"named-frames-heading-{zoom * 100:0}-percent.png");
        }
        mixer.Edit(configuration => configuration.Graph.Zoom = .5); await layout();
        bool? contrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            ConsoleTheme.SetPreviewHighContrast(true); mixer.RefreshPreviewTelemetry(); await layout();
            require(ReferenceEquals(workspace.FrameViews[id].FrameBorderBrush, SystemColors.WindowTextBrush)
                || ReferenceEquals(workspace.FrameViews[id].FrameBorderBrush, SystemColors.HighlightBrush),
                "High contrast uses Windows system frame ink rather than relying on the preset hue.");
            capture?.Invoke("named-frames-high-contrast.png");
        }
        finally { ConsoleTheme.SetPreviewHighContrast(contrast); }
        await layout();

        Select(outside.Id); workspace.AddSelectionToFrame(id); await layout();
        require(Frame(id).NodeIds.Count == 3 && workspace.FrameBounds(id).Contains(NodeBounds(outside.Id)) && Routing() == routing,
            "Adding selected nodes expands the existing frame without reconnecting any audio path.");
        Select(outside.Id);
        workspace.RemoveSelectionFromFrames(); await layout();
        require(Frame(id).NodeIds.ToHashSet(StringComparer.Ordinal).SetEquals([gain.Id, compressor.Id]) && Routing() == routing,
            "Removing members shrinks their frame while retaining the removed nodes and all routing.");
        require(mixer.Undo() && Frame(id).NodeIds.Contains(outside.Id) && mixer.Redo() && !Frame(id).NodeIds.Contains(outside.Id),
            "Membership removal participates in the same workspace Undo and Redo history.");
        await layout();

        // Lock is a layout guard. Selection remains useful and does not delete members.
        mixer.IsLayoutLocked = true;
        workspace.SelectFrame(id); before = FullGraph(); history = mixer.EditHistory.Entries.Count;
        workspace.BeginFrameMove(id, new(0, 0)); workspace.UpdateNodeMove(new(80, 40)); workspace.FinishNodeMove();
        workspace.RemoveFrame(id); workspace.RemoveSelectionFromFrames();
        workspace.RenameFrame(id, "Locked rename"); workspace.RecolorFrame(id, "amber");
        Select(outside.Id); workspace.AddSelectionToFrame(id);
        string? lockedCreate = workspace.CreateFrameFromSelection();
        require(FullGraph() == before && mixer.EditHistory.Entries.Count == history && lockedCreate is null,
            "Layout lock prevents frame movement, ungrouping, membership edits, renaming, recoloring and creation without changing nodes or history.");
        mixer.IsLayoutLocked = false; await layout();

        // Whole-frame clipboard transport gives both the frame and its nodes fresh identities.
        workspace.SelectFrame(id);
        var originalMembers = Frame(id).NodeIds.ToHashSet(StringComparer.Ordinal);
        string copied = workspace.CopySelection();
        var originalNodes = Graph().Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        require(workspace.PasteSelection(copied, new(1700, 700)), "A copied named group pastes through the actual workspace action.");
        await layout();
        var pastedNodes = Graph().Nodes.Where(node => !originalNodes.Contains(node.Id)).Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var pasted = Graph().Frames.Single(frame => frame.Id != id);
        require(pastedNodes.Count == 2 && pasted.NodeIds.ToHashSet(StringComparer.Ordinal).SetEquals(pastedNodes)
            && !pasted.NodeIds.Any(originalMembers.Contains) && pasted.Name == Frame(id).Name && pasted.ColorKey == Frame(id).ColorKey,
            "Pasting a complete frame preserves name and color while remapping its frame identity and every member to independent copies.");
        require(mixer.Undo() && Graph().Frames.Count == 1 && Graph().Nodes.Count == originalNodes.Count
            && mixer.Redo() && Graph().Frames.Count == 2 && Graph().Nodes.Count == originalNodes.Count + 2,
            "A pasted frame and its copied nodes undo and redo as a single atomic edit.");
        await layout();
        string pastedId = pasted.Id;
        workspace.SelectFrame(pastedId); before = EngineJson(); workspace.DeleteSelection(); await layout();
        require(!HasFrame(pastedId) && EngineJson() == before && pastedNodes.All(nodeId => Graph().Nodes.Any(node => node.Id == nodeId)),
            "Delete on a selected frame ungroups it without deleting its nodes, settings or cables.");
        require(mixer.Undo() && HasFrame(pastedId), "Undo restores an ungrouped frame with the same identity and members.");
        await layout();
        Select(pastedNodes.First()); workspace.DeleteSelection(); await layout();
        require(Frame(pastedId).NodeIds.Count == 1 && !Frame(pastedId).NodeIds.Contains(pastedNodes.First()),
            "Deleting one member prunes its stale frame membership while retaining surviving members.");
        Select(Frame(pastedId).NodeIds.Single()); workspace.DeleteSelection(); await layout();
        require(!HasFrame(pastedId) && !workspace.FrameViews.ContainsKey(pastedId),
            "Deleting a group's last node also removes its empty frame and stale rendered view.");
        require(mixer.Undo() && HasFrame(pastedId) && Frame(pastedId).NodeIds.Count == 1,
            "Undoing deletion of the last member restores the node together with its named frame.");
        await layout();
        // Remove this restored test copy before the single-member insertion check.
        Select(Frame(pastedId).NodeIds.Single()); workspace.DeleteSelection(); await layout();

        Select(outside.Id); string single = workspace.CreateFrameFromSelection()!; await layout();
        var member = View(outside.Id);
        Point from = workspace.SocketPosition(View(source.Id).FindSocket("audio", true)!);
        Point to = workspace.SocketPosition(View(output.Id).FindSocket("audio", false)!);
        Point cableCenter = new((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        Point origin = NodeBounds(outside.Id).TopLeft;
        Point destination = new(cableCenter.X - member.ActualWidth / 2, cableCenter.Y - member.ActualHeight / 2);
        Point dragStart = Screen(origin), dragEnd = dragStart + (destination - origin) * Graph().Zoom;
        before = Routing();
        workspace.BeginFrameMove(single, dragStart); workspace.UpdateNodeMove(dragEnd); await layout();
        require(workspace.FindInsertionCandidate(outside.Id) == "frame-through-cable" && workspace.InsertionPreviewId is null,
            "A one-member organizational group can cross an insertable cable without offering audio insertion.");
        workspace.FinishNodeMove(); await layout();
        require(Routing() == before && Graph().Edges.Count == 1 && HasFrame(single),
            "Dropping a one-member group over a cable changes only its layout and never splits or rewires that cable.");
        workspace.RemoveFrame(single); await layout();
        require(Graph().Nodes.Any(node => node.Id == outside.Id) && Routing() == before && HasFrame(id),
            "Explicit ungroup removes only its frame and leaves other groups, members and routing intact.");
        require(GraphDefaults.Validate(Graph()).Length == 0,
            "The complete named-group workflow ends with a valid audio graph and unchanged connection semantics.");
        workspace.SelectFrame(id); capture?.Invoke("named-frames-final.png");
    }

    private static GraphNode Node(string kind, string id, double x, double y)
    {
        var node = GraphDefaults.NewNode(kind, x, y);
        node.Id = id; node.Name = kind switch
        {
            "microphone" => "Example input", "output" => "Example output", "compressor" => "Voice dynamics", _ => "Voice level"
        };
        node.Collapsed = true; return node;
    }
    private static bool Close(double left, double right) => Math.Abs(left - right) < .000001;
    private static DependencyObject? DetachedHit(Visual root, Point point)
    {
        // A never-shown Window owns the detached renderer's resources. Effective
        // IsVisible is therefore false, and InputHitTest rejects the whole tree.
        // Match the existing socket/caption checks: test actual drawn geometry
        // with local Visibility and hit eligibility, not native input dispatch.
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(root,
            visual => visual is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(point));
        while (hit is not null && hit is not UIElement { IsEnabled: true }) hit = VisualTreeHelper.GetParent(hit);
        return hit;
    }
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject? current)
    {
        while (current is not null)
        {
            yield return current;
            current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
    }
    private static string DescribeHit(DependencyObject? hit) => hit is null ? "null"
        : string.Join(" > ", Ancestors(hit).Select(item => item.GetType().Name));
    private static IEnumerable<MenuItem> MenuItems(ItemsControl root)
    {
        foreach (var item in root.Items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var nested in MenuItems(item)) yield return nested;
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
