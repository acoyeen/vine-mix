using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

public sealed partial class GraphWorkspace
{
    private readonly Dictionary<string, GraphFrameView> _frames = [];
    private string? _selectedFrame;
    private bool _movingFrame;
    internal IReadOnlyDictionary<string, GraphFrameView> FrameViews => _frames;

    // Frames contain only membership and presentation. Their bounds are always
    // derived from the rendered nodes, including an in-progress node movement.
    internal Rect FrameBounds(string id) => Graph.Frames.FirstOrDefault(frame => frame.Id == id) is { } frame
        ? GetFrameBounds(frame, _zoom) : Rect.Empty;

    private Rect GetFrameBounds(GraphFrame frame, double zoom)
    {
        var members = Rect.Empty;
        foreach (string id in frame.NodeIds)
        {
            if (!_nodes.TryGetValue(id, out var node)) continue;
            double width = node.ActualWidth, height = node.ActualHeight;
            if (width <= 0 || height <= 0)
            {
                node.Measure(new Size(node.Width, double.PositiveInfinity));
                width = node.DesiredSize.Width; height = node.DesiredSize.Height;
            }
            if (width > 0 && height > 0)
                members.Union(new Rect(Canvas.GetLeft(node), Canvas.GetTop(node), width, height));
        }
        if (members.IsEmpty) return members;
        double padding = GraphFrameView.Padding, header = GraphFrameView.HeaderHeight(zoom);
        return new Rect(members.Left - padding, members.Top - padding - header,
            Math.Max(members.Width + padding * 2, GraphFrameView.MinimumWidth(zoom)), members.Height + padding * 2 + header);
    }

    private void RefreshFrames()
    {
        foreach (string id in _frames.Keys.Where(id => Graph.Frames.All(frame => frame.Id != id)).ToArray())
        {
            _frames[id].CancelRename(); _world.Children.Remove(_frames[id]); _frames.Remove(id);
            if (_selectedFrame == id) _selectedFrame = null;
        }
        foreach (var frame in Graph.Frames)
        {
            Rect bounds = GetFrameBounds(frame, _zoom);
            if (bounds.IsEmpty) continue;
            if (!_frames.TryGetValue(frame.Id, out var view))
            {
                view = new GraphFrameView(); string id = frame.Id;
                Panel.SetZIndex(view, -1);
                view.MouseLeftButtonDown += (_, args) => FramePressed(id, view, args);
                view.MouseRightButtonDown += (_, args) =>
                {
                    if (view.IsRenaming || HasCanvasPanGesture || Keyboard.IsKeyDown(Key.Space)) return;
                    SelectFrame(id); Focus();
                    var menu = CreateFrameMenu(id); menu.PlacementTarget = view; menu.IsOpen = true;
                    args.Handled = true;
                };
                _frames.Add(id, view); _world.Children.Add(view);
            }
            if (!_mixer.CanEditLayout) view.CancelRename();
            view.Update(frame.Name, frame.ColorKey, frame.NodeIds.Count, _zoom, _selectedFrame == frame.Id, !_mixer.CanEditLayout);
            Canvas.SetLeft(view, bounds.Left); Canvas.SetTop(view, bounds.Top);
            view.Width = bounds.Width; view.Height = bounds.Height;
        }
    }

    internal void SelectFrame(string id)
    {
        var frame = Graph.Frames.FirstOrDefault(frame => frame.Id == id);
        if (frame is null) return;
        SetNodeSelection(frame.NodeIds, raiseNewlySelected: false);
        BringNodesToFront(frame.NodeIds);
        _selectedFrame = id; RefreshFrames();
        _delete.ToolTip = "Ungroups the selected frame and keeps its nodes and connections.";
    }

    private void FramePressed(string id, GraphFrameView view, MouseButtonEventArgs args)
    {
        if (Keyboard.IsKeyDown(Key.Space) || IsEditingControl(args.OriginalSource as DependencyObject)) return;
        bool rename = args.ClickCount == 2 && args.GetPosition(view).Y <= view.HeaderElement.ActualHeight;
        if (!BeginFramePointerGesture(id, args.GetPosition(_viewport), Keyboard.Modifiers, rename)) return;
        if ((_dragged is not null || _lockedLayoutGestureStart is not null) && !_viewport.CaptureMouse())
            FinishGesture(cancel: true);
        args.Handled = true;
    }

    internal bool BeginFramePointerGesture(string id, Point viewportStart, ModifierKeys modifiers, bool rename = false)
    {
        // Modified background drags still reach the canvas's additive marquee.
        if (HasCanvasPanGesture || (modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0
            || Graph.Frames.All(frame => frame.Id != id)) return false;
        if (HasActiveGesture) CancelActiveGesture();
        SelectFrame(id); Focus();
        if (rename) BeginFrameRename(id);
        else if (_mixer.IsLayoutLocked)
        {
            BeginLockedLayoutGesture(viewportStart);
        }
        else
        {
            BeginFrameMove(id, viewportStart);
        }
        return true;
    }

    internal void BeginFrameMove(string id, Point viewportStart)
    {
        if (!RequireLayoutEdit()) return;
        var frame = Graph.Frames.FirstOrDefault(frame => frame.Id == id);
        if (frame is null || frame.NodeIds.Count == 0) return;
        SelectFrame(id);
        BeginNodeMove(frame.NodeIds[0], viewportStart, bringAnchorToFront: false);
        _movingFrame = _dragged is not null;
    }

    internal string? CreateFrameFromSelection()
    {
        string[] members = _selectedNodes.Where(id => Graph.Nodes.Any(node => node.Id == id)).ToArray();
        if (members.Length == 0 || !RequireLayoutEdit()) return null;
        // An exact existing group is selected instead of creating a duplicate.
        var existing = Graph.Frames.FirstOrDefault(frame => frame.NodeIds.ToHashSet().SetEquals(members));
        if (existing is not null) { SelectFrame(existing.Id); return existing.Id; }
        string id = Guid.NewGuid().ToString("N"), name = "Group";
        for (int index = 2; Graph.Frames.Any(frame => frame.Name == name); index++) name = "Group " + index;
        var created = new GraphFrame { Id = id, Name = name, ColorKey = "sage", NodeIds = members.ToList() };
        _mixer.Edit(configuration =>
        {
            foreach (var frame in configuration.Graph.Frames) frame.NodeIds.RemoveAll(members.Contains);
            configuration.Graph.Frames.RemoveAll(frame => frame.NodeIds.Count == 0);
            if (configuration.Graph.Frames.All(frame => frame.Id != id)) configuration.Graph.Frames.Add(created.Copy());
        });
        SelectFrame(id);
        SetMessage("Group created.");
        return id;
    }

    private void BeginFrameRename(string id)
    {
        if (!RequireLayoutEdit() || !_frames.TryGetValue(id, out var view)) return;
        view.BeginRename(name => RenameFrame(id, name));
    }

    internal void RenameFrame(string id, string name)
    {
        if (!RequireLayoutEdit()) return;
        string value = GraphFrames.NormalizeName(name);
        _mixer.Edit(configuration =>
        {
            if (configuration.Graph.Frames.FirstOrDefault(frame => frame.Id == id) is { } frame) frame.Name = value;
        });
    }

    internal void RecolorFrame(string id, string colorKey)
    {
        if (!RequireLayoutEdit()) return;
        _mixer.Edit(configuration =>
        {
            if (configuration.Graph.Frames.FirstOrDefault(frame => frame.Id == id) is { } frame) frame.ColorKey = colorKey;
        });
    }

    internal void RemoveFrame(string id)
    {
        if (!RequireLayoutEdit()) return;
        if (_selectedFrame == id) _selectedFrame = null;
        _mixer.Edit(configuration => configuration.Graph.Frames.RemoveAll(frame => frame.Id == id));
        SetMessage("Ungrouped. Nodes and connections kept.");
    }

    internal void AddSelectionToFrame(string id)
    {
        if (!RequireLayoutEdit() || Graph.Frames.All(frame => frame.Id != id)) return;
        string[] members = _selectedNodes.ToArray();
        _mixer.Edit(configuration =>
        {
            var target = configuration.Graph.Frames.FirstOrDefault(frame => frame.Id == id);
            if (target is null) return;
            foreach (var frame in configuration.Graph.Frames.Where(frame => frame.Id != id)) frame.NodeIds.RemoveAll(members.Contains);
            target.NodeIds = target.NodeIds.Concat(members).Distinct().ToList();
        });
        SelectFrame(id); SetMessage("Selected nodes added to the group.");
    }

    internal void RemoveSelectionFromFrames()
    {
        if (!RequireLayoutEdit()) return;
        string[] members = _selectedNodes.ToArray(); _selectedFrame = null;
        _mixer.Edit(configuration =>
        {
            foreach (var frame in configuration.Graph.Frames) frame.NodeIds.RemoveAll(members.Contains);
        });
        SetMessage("Selected nodes removed from their groups. Connections kept.");
    }

    internal MenuItem CreateGroupMenuItem()
    {
        var item = new MenuItem { Header = "Group selected nodes", InputGestureText = "Ctrl+G",
            IsEnabled = _mixer.CanEditLayout && _selectedNodes.Count > 0,
            ToolTip = NodeToolTip.Create("Group selected nodes", "Adds a named, colored frame around these nodes", "Organizes the canvas without changing sound or connections") };
        item.Click += (_, _) => CreateFrameFromSelection();
        return item;
    }

    private void AddFrameSelectionMenuItems(ContextMenu menu)
    {
        if (_selectedNodes.Count == 0) return;
        menu.Items.Add(new Separator()); menu.Items.Add(CreateGroupMenuItem());
        if (Graph.Frames.Count > 0)
        {
            var add = new MenuItem { Header = "Add to group", IsEnabled = _mixer.CanEditLayout };
            foreach (var frame in Graph.Frames)
            {
                string id = frame.Id;
                var item = new MenuItem { Header = frame.Name,
                    IsEnabled = _selectedNodes.Any(member => !frame.NodeIds.Contains(member)) };
                item.Click += (_, _) => AddSelectionToFrame(id); add.Items.Add(item);
            }
            menu.Items.Add(add);
        }
        if (Graph.Frames.Any(frame => frame.NodeIds.Any(_selectedNodes.Contains)))
        {
            var remove = new MenuItem { Header = "Remove from group", IsEnabled = _mixer.CanEditLayout };
            remove.Click += (_, _) => RemoveSelectionFromFrames(); menu.Items.Add(remove);
        }
    }

    internal ContextMenu CreateFrameMenu(string id)
    {
        var menu = TrackPracticeMenu(new ContextMenu());
        var frame = Graph.Frames.FirstOrDefault(frame => frame.Id == id);
        if (frame is null) return menu;
        var rename = new MenuItem { Header = "Rename group", InputGestureText = "F2", IsEnabled = _mixer.CanEditLayout };
        rename.Click += (_, _) => Dispatcher.BeginInvoke(new Action(() => BeginFrameRename(id)));
        menu.Items.Add(rename);
        var colors = new MenuItem { Header = "Group color", IsEnabled = _mixer.CanEditLayout };
        foreach (var color in GraphFramePalette.All)
        {
            string key = color.Key;
            var item = new MenuItem { Header = color.Name, IsCheckable = true, IsChecked = frame.ColorKey == key,
                Icon = new Rectangle { Width = 12, Height = 12, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(color.Accent) } };
            AutomationProperties.SetName(item, color.Name + " group color");
            item.Click += (_, _) => RecolorFrame(id, key); colors.Items.Add(item);
        }
        menu.Items.Add(colors); menu.Items.Add(new Separator());
        var select = new MenuItem { Header = "Select member nodes" };
        select.Click += (_, _) => SetNodeSelection(frame.NodeIds); menu.Items.Add(select);
        var copy = new MenuItem { Header = "Copy group and nodes", InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => { SelectFrame(id); CopyToClipboard(); }; menu.Items.Add(copy);
        var remove = new MenuItem { Header = "Ungroup (keep nodes)", InputGestureText = "Delete", IsEnabled = _mixer.CanEditLayout };
        remove.Click += (_, _) => RemoveFrame(id); menu.Items.Add(remove);
        MenuHoverHelp.Attach(menu);
        return menu;
    }

    private Rect IncludeFrameBounds(Rect nodeBounds, double zoom, IEnumerable<string> selected)
    {
        var ids = selected.ToHashSet();
        foreach (var frame in Graph.Frames.Where(frame => frame.NodeIds.All(ids.Contains)))
        {
            Rect bounds = GetFrameBounds(frame, zoom);
            if (!bounds.IsEmpty) nodeBounds.Union(bounds);
        }
        return nodeBounds;
    }
}
