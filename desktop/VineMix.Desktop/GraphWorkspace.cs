using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>The editor sends settings only. All audio work remains in the native engine.</summary>
public sealed class GraphWorkspace : UserControl
{
    public const double ToolbarHeight = 34;
    private readonly MixerViewModel _mixer;
    private bool _initialFrameQueued;
    private readonly Grid _viewport = new() { ClipToBounds = true, Background = Brushes.Transparent };
    private readonly Canvas _world = new() { Background = Brushes.Transparent, Width = 40000, Height = 40000, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly Canvas _cables = new() { IsHitTestVisible = true, Width = 40000, Height = 40000 };
    private readonly MatrixTransform _viewTransform = new();
    private readonly Dictionary<string, GraphNodeView> _nodes = [];
    private readonly Dictionary<string, WireVisual> _wires = [];
    private string _lastMessage = "";
    private bool _feedbackIsModelWarning;
    private readonly TextBlock _feedback = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _dismissFeedback = new() { Content = "Dismiss", MinHeight = 26, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(12, 0, 0, 0), Visibility = Visibility.Collapsed };
    private readonly DockPanel _feedbackContent = new() { LastChildFill = true, Margin = new Thickness(12, 4, 12, 4), Visibility = Visibility.Collapsed };
    private readonly Border _footerStatusHost = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 4, 12, 4) };
    private readonly Border _feedbackCountdown = new() { Height = 2, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Hidden, RenderTransformOrigin = new Point(0, 0) };
    private readonly ScaleTransform _feedbackCountdownScale = new(1, 1);
    private readonly System.Windows.Threading.DispatcherTimer _feedbackTimer = new();
    private AnimationClock? _feedbackCountdownClock;
    private double _feedbackDeadline;
    private const double FeedbackDurationSeconds = 5;
    private static double FeedbackNow => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private readonly Button _undo = new() { Content = "Undo", ToolTip = "Reverts the last workspace edit." };
    private readonly Button _redo = new() { Content = "Redo", ToolTip = "Restores the last undone workspace edit." };
    private readonly Button _history = new() { Content = "Activity", ToolTip = "Recent workspace edits and messages" };
    private readonly Button _zoomReset = new() { Width = 62, Height = 26, MinHeight = 26, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(4, 0, 4, 0), FontSize = 13, ToolTip = NodeToolTip.Create("Reset zoom", "Restores 100% canvas zoom") };
    private readonly TextBlock _countText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Margin = new Thickness(16, 0, 0, 0) };
    private static readonly double[] ZoomStops = [.3, .4, .5, .6, .75, .9, 1, 1.1, 1.25, 1.5, 1.75, 2];
    private readonly Border _empty;
    private readonly Button _add;
    private readonly Button _delete;
    private readonly Button _emptyAdd;
    private readonly CanvasGrid _grid;
    private readonly System.Windows.Shapes.Path _previewWire = new() { StrokeThickness = 2, StrokeDashArray = new DoubleCollection([3, 3]), IsHitTestVisible = false };
    private readonly HashSet<string> _selectedNodes = [];
    private string? _selectedEdge;
    private readonly Dictionary<string, Point> _dragOrigins = [];
    private readonly Canvas _selectionLayer = new() { IsHitTestVisible = false };
    private readonly Border _selectionBox = new() { Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromArgb(28, 220, 220, 220)) };
    private readonly Polyline _cutStroke = new() { StrokeThickness = 2, StrokeDashArray = new DoubleCollection([3, 3]), IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Dictionary<string, GraphEdge> _cutCandidates = [];
    private readonly Dictionary<string, GraphEdge> _cutEdges = [];
    private bool _cutting, _suppressContextMenu;
    private Point _lastCutPoint;
    private GraphEdge? _rewireOriginal;
    private Socket? _inputLinkSocket;
    private GraphEdge[] _inputLinkCandidates = [];
    private string? _inputLinkCandidate;
    private const double SocketSnapRadius = 18;
    private Socket? _snapSocket;
    private string? _insertionEdge;
    private bool _detachOnDrop;
    private HashSet<string> _boxOriginalSelection = [];
    private bool _boxSelecting;
    private string? _lastPastedPayload;
    private int _pasteRepeat;
    private Socket? _pendingSocket;
    private Point _pointer, _gestureStart, _originalPosition;
    private GraphNodeView? _dragged;
    private Point? _lockedLayoutGestureStart;
    private bool _lockedLayoutGestureNotified;
    private bool _panning, _socketDragging, _moved, _loaded, _syncing;
    private MouseButton? _panButton;
    private const double RightPanThreshold = 6; // Viewport DIPs, independent of canvas zoom.
    private double _panX = 20, _panY = 20, _zoom = 1;
    private int _wheelRemainder;
    private double _displayedZoom = double.NaN;
    private Point? _previewFrom, _previewTo;
    private string? _previewBrushKey;
    private static readonly DoubleCollection MutedWireDashes = CreateMutedWireDashes();

    // In-memory render diagnostics: no timers, settings, or engine access.
    internal long ViewTransformUpdates { get; private set; }
    internal long WireGeometryBuilds { get; private set; }
    internal long WireGeometryPasses { get; private set; }
    internal long GridTileRebuilds => _grid.TileRebuilds;
    internal long GridRenderCount => _grid.RenderCount;
    internal double GridSpacing => _grid.Spacing;
    internal int GridDotCountBound => _grid.DotCountBound;
    internal IReadOnlyCollection<string> SelectedNodeIds => _selectedNodes.ToArray();
    internal Rect SelectionBoxBounds => new(Canvas.GetLeft(_selectionBox), Canvas.GetTop(_selectionBox), _selectionBox.Width, _selectionBox.Height);
    internal bool SelectionBoxVisible => _selectionBox.Visibility == Visibility.Visible;
    internal string? InsertionPreviewId => _insertionEdge;
    internal bool HasConnectionPreview => _pendingSocket is not null;
    internal bool IsNodeDragging => _dragged is not null;
    internal string? SnapPreviewNodeId => _snapSocket?.NodeId;
    internal string? RewirePreviewId => _rewireOriginal?.Id;
    internal string? InputLinkPreviewId => _inputLinkCandidate;
    internal bool HasCableCutPreview => _cutting;
    internal bool HasCanvasPanGesture => _panButton is not null;
    internal bool IsCanvasPanning => _panning;
    internal string FeedbackText => _feedback.Text;
    internal bool HasFeedback => _dismissFeedback.Visibility == Visibility.Visible;
    internal FrameworkElement CountdownElement => _feedbackCountdown;
    internal double FeedbackCountdownFraction => _feedbackCountdownScale.ScaleX;
    internal bool FeedbackCountdownAnimationActive => _feedbackCountdownClock is not null;
    internal bool FeedbackCountdownTimerRunning => _feedbackTimer.IsEnabled;
    internal bool? PreviewFeedbackMotion { get; set; }
    private bool FeedbackMotionEnabled => (_mixer.IsPreview ? PreviewFeedbackMotion : null) ?? SystemParameters.ClientAreaAnimation;
    internal IReadOnlyCollection<string> CutPreviewIds => _cutEdges.Keys.ToArray();
    internal double ConnectionHitWidth(string id) => _wires.TryGetValue(id, out var wire) ? wire.Hit.StrokeThickness * _zoom : 0;
    internal object? ConnectionToolTip(string id) => _wires.TryGetValue(id, out var wire) ? wire.Hit.ToolTip : null;
    internal (System.Windows.Shapes.Path Visible, System.Windows.Shapes.Path Hit, System.Windows.Shapes.Path Outline)? ConnectionPaths(string id)
        => _wires.TryGetValue(id, out var wire) ? (wire.Visible, wire.Hit, wire.Outline) : null;

    private readonly bool _useSystemClipboard;
    private string? _practiceClipboard;
    private readonly List<ContextMenu> _practiceMenus = [];
    internal bool HasActiveGesture => _pendingSocket is not null || _dragged is not null
        || _boxSelecting || _cutting || _panButton is not null || _socketDragging || _lockedLayoutGestureStart is not null;
    internal bool ClosePracticeMenus()
    {
        bool closed = false;
        foreach (var menu in _practiceMenus.ToArray())
            if (menu.IsOpen) { menu.IsOpen = false; closed = true; }
        return closed;
    }
    internal void ResetPracticeState()
    {
        if (_useSystemClipboard) throw new InvalidOperationException("Practice reset is available only in an isolated workspace.");
        ClosePracticeMenus(); CancelActiveGesture();
        _practiceClipboard = null; _lastPastedPayload = null; _pasteRepeat = 0;
        SetNodeSelection([]);
        SetMessage("Practice reset. Connect, select or move a sample node.");
    }
    private ContextMenu TrackPracticeMenu(ContextMenu menu)
    {
        if (!_useSystemClipboard)
        {
            _practiceMenus.Add(menu);
            menu.Closed += (_, _) => _practiceMenus.Remove(menu);
        }
        return menu;
    }

    public GraphWorkspace(MixerViewModel mixer, bool useSystemClipboard = true)
    {
        _useSystemClipboard = useSystemClipboard;
        _mixer = mixer; Focusable = true; UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new GridLength(ToolbarHeight) }); root.RowDefinitions.Add(new());
        root.RowDefinitions.Add(new() { Height = GridLength.Auto, MinHeight = 28 });
        var toolbar = new DockPanel { Margin = new Thickness(8, 4, 8, 4), LastChildFill = false };
        _add = Button("Add", "Opens the node library."); _add.Click += (_, _) => ShowAddMenu(_add, VisibleCenter()); toolbar.Children.Add(_add);
        _delete = Button("Remove", "Removes the selected nodes or cable."); _delete.Margin = new Thickness(7, 0, 0, 0); _delete.Click += (_, _) => DeleteSelection(); toolbar.Children.Add(_delete);
        void AddHistoryButton(Button button)
        {
            // A disabled history button must remain client area rather than
            // turning into a window-drag target in the extended title bar.
            var hitSurface = new Border { Child = button, Background = Brushes.Transparent };
            hitSurface.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(nameof(ToolTip)) { Source = button });
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(hitSurface, true);
            toolbar.Children.Add(hitSurface);
        }
        _undo.Click += (_, _) => RestoreHistory(redo: false); AddHistoryButton(_undo);
        _redo.Click += (_, _) => RestoreHistory(redo: true); AddHistoryButton(_redo);
        _history.Click += (_, _) => ShowHistoryMenu(); toolbar.Children.Add(_history);
        _countText.Margin = new Thickness(16, 0, 0, 0); _countText.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush"); toolbar.Children.Add(_countText);
        var zoomTools = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(zoomTools, Dock.Right);
        var fit = Button("Fit", "Brings every node into view."); fit.Click += (_, _) => FitAll(); zoomTools.Children.Add(fit);
        var minus = Button("−", "Zoom out"); minus.Width = 32; minus.Margin = new Thickness(8, 0, 0, 0); minus.Click += (_, _) => StepZoom(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2), -1); zoomTools.Children.Add(minus);
        _zoomReset.Click += (_, _) => ResetZoom(); AutomationProperties.SetName(_zoomReset, "Reset canvas zoom to 100 percent"); zoomTools.Children.Add(_zoomReset);
        var plus = Button("+", "Zoom in"); plus.Width = 32; plus.Click += (_, _) => StepZoom(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2), 1); zoomTools.Children.Add(plus);
        toolbar.Children.Add(zoomTools); root.Children.Add(toolbar);
        foreach (var button in new[] { _add, _delete, _undo, _redo, _history, fit, minus, _zoomReset, plus })
        {
            button.SetResourceReference(StyleProperty, "WorkspaceToolbarButton");
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        }
        _grid = new CanvasGrid(); _world.RenderTransform = _viewTransform; _viewport.Children.Add(_grid); _viewport.Children.Add(_world); _world.Children.Add(_cables); _cables.Children.Add(_previewWire);
        TextOptions.SetTextFormattingMode(_world, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(_world, TextRenderingMode.Grayscale);
        _empty = new Border { Padding = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var emptyText = new StackPanel();
        _add.SetResourceReference(StyleProperty, "PrimaryWorkspaceToolbarButton");
        var start = _emptyAdd = Button("+  Add node", "Opens the node library."); start.Click += (_, _) => ShowAddMenu(start, VisibleCenter()); emptyText.Children.Add(start);
        var emptyHint = Text("Connect an input to an output.", 13); emptyHint.Margin = new Thickness(0, 10, 0, 0); emptyHint.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush"); emptyText.Children.Add(emptyHint); _empty.Child = emptyText; _viewport.Children.Add(_empty);
        _selectionBox.SetResourceReference(Border.BorderBrushProperty, "NodeSelectedBrush");
        _cutStroke.SetResourceReference(Shape.StrokeProperty, "NodeSelectedBrush");
        _selectionLayer.Children.Add(_selectionBox); _selectionLayer.Children.Add(_cutStroke); _viewport.Children.Add(_selectionLayer);
        Grid.SetRow(_viewport, 1); root.Children.Add(_viewport);
        // Keep feedback outside the zoomed canvas. Long messages wrap instead
        // of hiding their recovery instructions in a tooltip or activity menu.
        DockPanel.SetDock(_dismissFeedback, Dock.Right);
        _dismissFeedback.SetResourceReference(StyleProperty, "WorkspaceToolbarButton");
        AutomationProperties.SetName(_dismissFeedback, "Dismiss workspace message");
        _dismissFeedback.Click += (_, _) => DismissMessage();
        _feedbackContent.Children.Add(_dismissFeedback); _feedbackContent.Children.Add(_feedback);
        AutomationProperties.SetLiveSetting(_feedback, AutomationLiveSetting.Polite);
        var footer = new Grid { MinHeight = 34, Focusable = false };
        footer.SetResourceReference(Panel.BackgroundProperty, "ConsoleWindowBrush");
        var footerBorder = new Border { BorderThickness = new Thickness(0, 1, 0, 0), IsHitTestVisible = false };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
        _feedbackCountdown.SetResourceReference(Border.BackgroundProperty, "ConsoleMeterLowBrush");
        _feedbackCountdown.RenderTransform = _feedbackCountdownScale;
        footer.Children.Add(footerBorder); footer.Children.Add(_footerStatusHost); footer.Children.Add(_feedbackContent); footer.Children.Add(_feedbackCountdown);
        Grid.SetRow(footer, 2); root.Children.Add(footer);
        Content = root;
        _viewport.PreviewMouseWheel += OnWheel; _viewport.PreviewMouseLeftButtonDown += CanvasPreviewMouseLeftButtonDown; _viewport.MouseDown += CanvasMouseDown; _viewport.MouseMove += CanvasMouseMove; _viewport.MouseUp += CanvasMouseUp; _viewport.MouseLeave += (_, _) => { if (_pendingSocket is null) SetSnapSocket(null); }; _viewport.LostMouseCapture += (_, _) => HandleCaptureLoss();
        _viewport.PreviewMouseRightButtonDown += CanvasPreviewMouseRightButtonDown;
        _viewport.PreviewMouseRightButtonUp += CanvasPreviewMouseRightButtonUp;
        _viewport.ContextMenuOpening += (_, args) => { if (_suppressContextMenu && args.CursorLeft >= 0) args.Handled = true; };
        PreviewKeyDown += WorkspaceKeyDown;
        _feedbackTimer.Tick += (_, _) => ResumeFeedbackCountdown();
        Loaded += (_, _) => { if (_loaded) return; _loaded = true; _mixer.GraphChanged += GraphChanged; _mixer.PropertyChanged += ModelPropertyChanged; ConsoleTheme.Changed += ThemeChanged; Refresh(); ResumeFeedbackCountdown(); };
        _viewport.SizeChanged += (_, _) => QueueInitialFrame();
        Unloaded += (_, _) => { CancelActiveGesture(); StopFeedbackCountdown(); _loaded = false; _mixer.GraphChanged -= GraphChanged; _mixer.PropertyChanged -= ModelPropertyChanged; ConsoleTheme.Changed -= ThemeChanged; };
        DismissMessage();
        AutomationProperties.SetName(this, "Audio routing canvas");
        AutomationProperties.SetHelpText(this, "Drag an existing cable from its input to move it; release on empty canvas to remove it. For multiple incoming cables, select one first or follow its curve away from the input. Ctrl and right-drag cuts crossed cables. Drag empty space to box-select nodes. Shift-click toggles a node in the selection; Shift-drag adds to it. Drag any selected header to move the group. Alt-drag a single processing node to detach it while keeping its path connected. Drop an unconnected processing node onto a highlighted audio cable to insert it. Ctrl+C copies nodes and their internal connections; Ctrl+V pastes. Right-click also provides Copy, Paste, Detach and Delete. Ctrl+A selects all nodes. F frames the selected nodes; Home or Fit shows all nodes. Middle mouse or Space and drag pans. Enable Right-click drag to pan in Settings to pan empty canvas with the right button; release without dragging for the menu. Scroll zooms; Ctrl+0 resets zoom. Delete removes the selection; Escape cancels a gesture or connection.");
    }

    internal MixerViewModel Mixer => _mixer;
    internal GraphConfiguration Graph => _mixer.GraphConfig;
    internal static TextBlock Text(string text, double size = 13, bool bold = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal }; block.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush"); return block;
    }
    internal static Button Button(string text, string tooltip) => new() { Content = text, ToolTip = tooltip, MinHeight = 26, Height = 26, Padding = new Thickness(10, 2, 10, 2), FontSize = 13 };
    private Point VisibleCenter() => ToWorld(new Point(_viewport.ActualWidth * .45, _viewport.ActualHeight * .35));
    private Point ToWorld(Point point) => new((point.X - _panX) / _zoom, (point.Y - _panY) / _zoom);
    private void GraphChanged(object? sender, EventArgs args) => Refresh();
    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MixerViewModel.IsLayoutLocked))
        {
            if (_mixer.IsLayoutLocked || _lockedLayoutGestureStart is not null) CancelActiveGesture();
            Refresh();
            SetMessage(_mixer.IsLayoutLocked
                ? "Layout locked. Sound controls, selection, pan and zoom remain available."
                : "Layout unlocked. Nodes and connections can be edited.");
        }
        if (args.PropertyName is nameof(MixerViewModel.CanEdit) or nameof(MixerViewModel.ReducedAnimation)) Refresh();
        if (args.PropertyName is nameof(MixerViewModel.CanUndo) or nameof(MixerViewModel.CanRedo)
            or nameof(MixerViewModel.UndoDescription) or nameof(MixerViewModel.RedoDescription)) UpdateHistoryButtons();
        if (args.PropertyName == nameof(MixerViewModel.Warning))
        {
            if (!string.IsNullOrWhiteSpace(_mixer.Warning))
            { SetMessage(_mixer.Warning, isError: true); _feedbackIsModelWarning = true; }
            else if (_feedbackIsModelWarning) DismissMessage();
        }
    }
    private void ThemeChanged(object? sender, EventArgs args) { _grid.InvalidateVisual(); Refresh(); ResumeFeedbackCountdown(); }

    private void Refresh()
    {
        if (_syncing) return; _syncing = true;
        try
        {
            var graph = Graph;
            bool wireAnchorsChanged = false;
            if (!HasCanvasPanGesture && _dragged is null && !_boxSelecting) { _panX = double.IsFinite(graph.PanX) ? graph.PanX : 20; _panY = double.IsFinite(graph.PanY) ? graph.PanY : 20; _zoom = Math.Clamp(double.IsFinite(graph.Zoom) ? graph.Zoom : 1, .3, 2); ApplyTransform(); }
            foreach (var missing in _nodes.Keys.Where(id => graph.Nodes.All(n => n.Id != id)).ToArray()) { _world.Children.Remove(_nodes[missing]); _nodes.Remove(missing); _selectedNodes.Remove(missing); _dragOrigins.Remove(missing); wireAnchorsChanged = true; }
            foreach (var node in graph.Nodes)
            {
                if (_nodes.TryGetValue(node.Id, out var previous) && !previous.MatchesPresentation(node))
                {
                    _world.Children.Remove(previous); _nodes.Remove(node.Id); wireAnchorsChanged = true;
                }
                if (!_nodes.TryGetValue(node.Id, out var view)) { view = new GraphNodeView(this, node); _nodes.Add(node.Id, view); _world.Children.Add(view); view.SetViewScale(_zoom); wireAnchorsChanged = true; }
                view.Apply(node, _mixer.GraphStatus.FirstOrDefault(s => s.Id == node.Id), _selectedNodes.Contains(node.Id));
                if (!_dragOrigins.ContainsKey(node.Id))
                {
                    if (Canvas.GetLeft(view) != node.X) { Canvas.SetLeft(view, node.X); wireAnchorsChanged = true; }
                    if (Canvas.GetTop(view) != node.Y) { Canvas.SetTop(view, node.Y); wireAnchorsChanged = true; }
                }
            }
            foreach (var missing in _wires.Keys.Where(id => graph.Edges.All(e => e.Id != id)).ToArray()) { _cables.Children.Remove(_wires[missing].Visible); _cables.Children.Remove(_wires[missing].Hit); _cables.Children.Remove(_wires[missing].Outline); _wires.Remove(missing); if (_selectedEdge == missing) _selectedEdge = null; }
            foreach (var edge in graph.Edges)
            {
                if (!_wires.ContainsKey(edge.Id))
                {
                    var visible = new System.Windows.Shapes.Path { IsHitTestVisible = false, StrokeThickness = 2.5 };
                    var outline = new System.Windows.Shapes.Path { IsHitTestVisible = false, StrokeThickness = 4.5 }; outline.SetResourceReference(Shape.StrokeProperty, "NodeCanvasBrush");
                    var hit = new System.Windows.Shapes.Path { Stroke = Brushes.Transparent, StrokeThickness = 14, Cursor = Cursors.Hand,
                        Stretch = Stretch.None, UseLayoutRounding = false, SnapsToDevicePixels = false };
                    string id = edge.Id; hit.MouseLeftButtonDown += (_, e) => WirePressed(id, e); hit.MouseRightButtonDown += (_, e) => { SelectEdge(id); ShowEdgeMenu(hit, id); e.Handled = true; };
                    _cables.Children.Add(outline); _cables.Children.Add(visible); _cables.Children.Add(hit); _wires.Add(id, new(visible, hit, outline));
                    wireAnchorsChanged = true;
                }
                var wire = _wires[edge.Id];
                if (wire.SourceId != edge.From || wire.TargetId != edge.To || wire.Kind != edge.Kind)
                { wire.SourceId = edge.From; wire.TargetId = edge.To; wire.Kind = edge.Kind; wireAnchorsChanged = true; }
                UpdateConnectionDetails(edge, wire);
                UpdateWireAppearance(edge, wire);
            }
            _emptyAdd.IsEnabled = _add.IsEnabled = _mixer.CanEditLayout && graph.Nodes.Count < 32;
            _delete.IsEnabled = _mixer.CanEditLayout && (_selectedNodes.Count > 0 || _selectedEdge is not null);
            UpdateHistoryButtons();
            _empty.Visibility = graph.Nodes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _countText.Text = $"{graph.Nodes.Count} nodes" + (_selectedNodes.Count > 0 ? $" · {_selectedNodes.Count} selected" : "");
            _countText.ToolTip = $"{graph.Edges.Count} connections · 32 node limit";
            if (wireAnchorsChanged) UpdateWires();
            else UpdatePreview();
        }
        finally { _syncing = false; }
        QueueInitialFrame();
    }
    private void QueueInitialFrame()
    {
        if (_initialFrameQueued || !_mixer.NeedsInitialFrame || !IsLoaded) return;
        _initialFrameQueued = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _initialFrameQueued = false;
            if (!_mixer.NeedsInitialFrame || !_mixer.CanEdit || _nodes.Count == 0 || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
            FitAll(); _mixer.InitialFrameCompleted();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void ApplyTransform()
    {
        // Render transforms run after layout rounding. Align the translation
        // to the current monitor's device pixels as well, including at 100%.
        var dpi = VisualTreeHelper.GetDpi(this);
        _panX = Math.Round(_panX * dpi.DpiScaleX) / dpi.DpiScaleX;
        _panY = Math.Round(_panY * dpi.DpiScaleY) / dpi.DpiScaleY;
        var matrix = new Matrix(_zoom, 0, 0, _zoom, _panX, _panY);
        if (_viewTransform.Matrix != matrix) { _viewTransform.Matrix = matrix; ViewTransformUpdates++; }
        _grid.SetView(_panX, _panY, _zoom);
        if (_displayedZoom != _zoom)
        {
            _displayedZoom = _zoom; _zoomReset.Content = $"{_zoom * 100:0}%";
            AutomationProperties.SetHelpText(_zoomReset, $"Current zoom {_zoom * 100:0} percent. Activate to return to 100 percent.");
            foreach (var view in _nodes.Values) view.SetViewScale(_zoom);
            foreach (var pair in _wires) SetWireWidth(pair.Key, pair.Value);
        }
        if (_snapSocket is not null) SetSnapSocket(IsCurrentSocket(_snapSocket) ? _snapSocket : null);
    }
    private void PersistView()
    {
        double x = _panX, y = _panY, z = _zoom;
        if (Graph.PanX == x && Graph.PanY == y && Graph.Zoom == z) return;
        _mixer.SetCanvasView(x, y, z);
    }
    private void ZoomAt(Point anchor, double zoom)
    {
        double next = Math.Clamp(zoom, .3, 2);
        if (_zoom == next) return;
        var world = ToWorld(anchor); _zoom = next; _panX = anchor.X - world.X * _zoom; _panY = anchor.Y - world.Y * _zoom; ApplyTransform(); PersistView();
    }
    internal void ResetZoom() { _wheelRemainder = 0; ZoomAt(new Point(_viewport.ActualWidth / 2, _viewport.ActualHeight / 2), 1); }
    private void StepZoom(Point anchor, int steps)
    {
        // Deliberate stops always include 100%; repeated input does not drift
        // through arbitrary fractional scales that make small text hard to read.
        double next = _zoom;
        for (int step = 0; step < Math.Min(Math.Abs((long)steps), ZoomStops.Length); step++)
            next = steps > 0
                ? ZoomStops.FirstOrDefault(value => value > next + .0001, ZoomStops[^1])
                : ZoomStops.LastOrDefault(value => value < next - .0001, ZoomStops[0]);
        ZoomAt(anchor, next);
    }
    internal void ApplyWheelDelta(Point anchor, int delta)
    {
        if (delta == 0) return;
        // High-resolution wheels can report a fraction of the standard 120
        // units per notch. Accumulate them instead of zooming a whole step for
        // every tiny event; a multi-notch event is applied in one transform.
        if (Math.Sign(delta) != Math.Sign(_wheelRemainder)) _wheelRemainder = 0;
        long accumulated = (long)_wheelRemainder + delta;
        int steps = (int)Math.Clamp(accumulated / 120, -ZoomStops.Length, ZoomStops.Length);
        _wheelRemainder = (int)(accumulated % 120);
        if (steps != 0) StepZoom(anchor, steps);
    }
    public void FitAll()
    {
        _wheelRemainder = 0;
        if (_nodes.Count == 0) { _panX = 20; _panY = 20; _zoom = 1; ApplyTransform(); PersistView(); return; }
        double availableWidth = Math.Max(0, _viewport.ActualWidth - 64), availableHeight = Math.Max(0, _viewport.ActualHeight - 54);
        // Every node has fixed world-space dimensions at every zoom. Settle any
        // pending control changes once, then fit those bounds.
        _world.UpdateLayout();
        var bounds = Rect.Empty;
        foreach (var view in _nodes.Values)
        {
            view.Measure(new Size(view.Width, double.PositiveInfinity));
            bounds.Union(new Rect(Canvas.GetLeft(view), Canvas.GetTop(view), view.Width, view.DesiredSize.Height));
        }
        _zoom = Math.Clamp(Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height), .3, 1);
        _panX = (_viewport.ActualWidth - bounds.Width * _zoom) / 2 - bounds.Left * _zoom;
        _panY = (_viewport.ActualHeight - bounds.Height * _zoom) / 2 - bounds.Top * _zoom;
        ApplyTransform(); PersistView();
    }
    private bool CanFrameSelection => _dragged is null && _lockedLayoutGestureStart is null && !HasCanvasPanGesture && !_boxSelecting && !_cutting
        && _pendingSocket is null && !_socketDragging && !IsFrameSelectionPointerBusy(Mouse.Captured) && !_mixer.EditHistory.HasGestureEdit;
    internal bool CanOpenNodeSearch => CanFrameSelection;
    internal bool FindAndFrameNode(string id)
    {
        if (!CanFrameSelection || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0
            || !Graph.Nodes.Any(node => node.Id == id) || !_nodes.TryGetValue(id, out var view)) return false;
        _world.UpdateLayout();
        if (view.RenderSize.Width <= 0 || view.RenderSize.Height <= 0) return false;
        SelectNode(id);
        return FrameSelection();
    }
    // A menu may retain capture while invoking its command. That capture is
    // navigation, unlike a captured dial, selector, slider or canvas gesture.
    internal static bool IsFrameSelectionPointerBusy(IInputElement? captured) => captured is not (null or MenuBase or MenuItem);
    internal bool FrameSelection()
    {
        if (!CanFrameSelection) return false;
        var selected = _selectedNodes.Where(id => Graph.Nodes.Any(node => node.Id == id))
            .Select(id => _nodes.GetValueOrDefault(id)).OfType<GraphNodeView>().ToArray();
        if (selected.Length == 0) { SetMessage("Select one or more nodes to frame."); return false; }
        if (_viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return false;
        // Settle layout before using the rendered controls, including taller
        // processors and nodes whose controls have just changed size.
        _world.UpdateLayout();
        var bounds = Rect.Empty;
        foreach (var view in selected)
        {
            double x = Canvas.GetLeft(view), y = Canvas.GetTop(view);
            if (!double.IsFinite(x) || !double.IsFinite(y) || view.RenderSize.Width <= 0 || view.RenderSize.Height <= 0) continue;
            bounds.Union(new Rect(new Point(x, y), view.RenderSize));
        }
        if (bounds.IsEmpty) return false;
        _wheelRemainder = 0;
        double availableWidth = Math.Max(0, _viewport.ActualWidth - 64), availableHeight = Math.Max(0, _viewport.ActualHeight - 54);
        _zoom = Math.Clamp(Math.Min(availableWidth / bounds.Width, availableHeight / bounds.Height), .3, 1);
        _panX = (_viewport.ActualWidth - bounds.Width * _zoom) / 2 - bounds.Left * _zoom;
        _panY = (_viewport.ActualHeight - bounds.Height * _zoom) / 2 - bounds.Top * _zoom;
        ApplyTransform(); PersistView();
        return true;
    }
    internal bool TryFrameSelectionShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused)
    {
        // A plain F belongs to the canvas only. Focused editors, node controls,
        // menus and toolbar buttons keep their own keyboard behavior.
        if (key != Key.F || modifiers != ModifierKeys.None || !ReferenceEquals(focused, this) || !CanFrameSelection) return false;
        FrameSelection(); return true;
    }
    internal MenuItem CreateFrameSelectionMenuItem()
    {
        var frame = new MenuItem { Header = "Frame selection", InputGestureText = "F",
            IsEnabled = CanFrameSelection && _selectedNodes.Any(id => _nodes.ContainsKey(id) && Graph.Nodes.Any(node => node.Id == id)),
            ToolTip = NodeToolTip.Create("Frame selection", "Brings the selected nodes into view") };
        frame.Click += (_, _) => FrameSelection();
        return frame;
    }
    private void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (_dragged is not null || _lockedLayoutGestureStart is not null || _boxSelecting || HasCanvasPanGesture || _cutting || _socketDragging) { args.Handled = true; return; }
        var anchor = args.GetPosition(_viewport);
        DependencyObject? source = args.OriginalSource as DependencyObject, hovered = Mouse.DirectlyOver as DependencyObject;
        bool openDropDown = _nodes.Values.Any(node => node.HasOpenDropDown);
        bool overDropDown = openDropDown && _nodes.Values.Any(node => node.IsPointerOverOpenDropDown());
        if (openDropDown && !overDropDown && Mouse.Captured is ComboBox { IsDropDownOpen: true } captured
            && (ReferenceEquals(source, captured) || ReferenceEquals(hovered, captured))
            && new Rect(_viewport.RenderSize).Contains(anchor))
        {
            // An open ComboBox captures its subtree. Outside the menu, use the
            // actual canvas hit only for capture-retargeted sources. Preserve
            // genuine popup/editor sources, including a separate context menu.
            var hit = _viewport.InputHitTest(anchor) as DependencyObject;
            if (ReferenceEquals(source, captured)) source = hit;
            if (ReferenceEquals(hovered, captured)) hovered = hit;
        }
        if (TryZoomFromWheel(anchor, args.Delta, source, hovered, overDropDown)) args.Handled = true;
    }
    internal bool TryZoomFromWheel(Point anchor, int delta, DependencyObject? source, DependencyObject? hovered, bool pointerOverOpenDropDown = false)
    {
        if (pointerOverOpenDropDown || IsWheelPopupSource(source) || IsWheelPopupSource(hovered))
        {
            _wheelRemainder = 0;
            // This is the preview route: leave the event unhandled so the
            // dropdown's own ScrollViewer can scroll, including at its ends.
            return false;
        }
        ApplyWheelDelta(anchor, delta);
        return true;
    }
    internal static bool IsWheelPopupSource(DependencyObject? source)
    {
        var pending = new Stack<DependencyObject>();
        var visited = new HashSet<DependencyObject>();
        if (source is not null) pending.Push(source);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current)) continue;
            // Every ordinary node surface zooms, including closed selectors,
            // text editors, sliders, dials and the EQ. Only open popup/menu
            // contents retain wheel scrolling. A TextBox's internal ScrollViewer
            // must not turn that text field into a dead area for canvas zoom.
            if (current is ComboBox { IsDropDownOpen: true } or ComboBoxItem or Popup or MenuBase or MenuItem) return true;
            // Popup content lives in another visual tree. Its logical/template
            // ownership still identifies the dropdown before it reaches ComboBox.
            void Visit(DependencyObject? parent) { if (parent is not null) pending.Push(parent); }
            if (current is Visual or System.Windows.Media.Media3D.Visual3D) Visit(VisualTreeHelper.GetParent(current));
            Visit(LogicalTreeHelper.GetParent(current));
            if (current is FrameworkElement element) { Visit(element.Parent); Visit(element.TemplatedParent); }
            else if (current is FrameworkContentElement content) { Visit(content.Parent); Visit(content.TemplatedParent); }
        }
        return false;
    }
    internal void SetMessage(string text, bool isError = false)
    {
        if (string.IsNullOrWhiteSpace(text)) { DismissMessage(); return; }
        _feedbackIsModelWarning = false;
        _lastMessage = text;
        string display = isError ? "Attention: " + text : text;
        bool changed = _feedback.Text != display || !HasFeedback;
        _feedback.Text = display;
        _feedback.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ConsoleAlertTextBrush" : "ConsoleTextBrush");
        _dismissFeedback.Visibility = Visibility.Visible;
        _feedbackContent.Visibility = Visibility.Visible;
        _footerStatusHost.Visibility = Visibility.Collapsed;
        _feedbackDeadline = FeedbackNow + FeedbackDurationSeconds;
        StartFeedbackCountdown(FeedbackDurationSeconds);
        if (changed && IsLoaded)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(_feedback) ?? FrameworkElementAutomationPeer.CreatePeerForElement(_feedback);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
    internal void DismissMessage()
    {
        StopFeedbackCountdown();
        _feedbackDeadline = 0;
        _feedbackCountdownScale.ScaleX = 0;
        _feedbackCountdown.Visibility = Visibility.Hidden;
        _feedbackIsModelWarning = false;
        _feedback.Text = "";
        _feedback.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        _dismissFeedback.Visibility = Visibility.Collapsed;
        _feedbackContent.Visibility = Visibility.Collapsed;
        _footerStatusHost.Visibility = Visibility.Visible;
    }
    internal void SetFooterStatus(FrameworkElement status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!ReferenceEquals(_footerStatusHost.Child, status)) _footerStatusHost.Child = status;
    }
    private void StartFeedbackCountdown(double remainingSeconds)
    {
        StopFeedbackCountdown();
        _feedbackCountdown.Visibility = Visibility.Visible;
        double fraction = Math.Clamp(remainingSeconds / FeedbackDurationSeconds, 0, 1);
        _feedbackCountdownScale.ScaleX = FeedbackMotionEnabled ? fraction : 1;
        if (!_loaded) return;
        _feedbackTimer.Interval = TimeSpan.FromSeconds(remainingSeconds);
        _feedbackTimer.Start();
        if (!FeedbackMotionEnabled) return;
        var animation = new DoubleAnimation(fraction, 0, TimeSpan.FromSeconds(remainingSeconds)) { FillBehavior = FillBehavior.HoldEnd };
        _feedbackCountdownClock = (AnimationClock)animation.CreateClock(true);
        _feedbackCountdownScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, _feedbackCountdownClock);
    }
    private void ResumeFeedbackCountdown()
    {
        if (!HasFeedback || !_loaded) return;
        double remainingSeconds = _feedbackDeadline - FeedbackNow;
        if (remainingSeconds <= 0) DismissMessage();
        else StartFeedbackCountdown(remainingSeconds);
    }
    private void StopFeedbackCountdown()
    {
        _feedbackTimer.Stop();
        _feedbackCountdownClock?.Controller?.Remove(); _feedbackCountdownClock = null;
        _feedbackCountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
    }
    internal void SeekFeedbackCountdown(double elapsedSeconds)
    {
        if (!_mixer.IsPreview) throw new InvalidOperationException("Only detached preview checks may seek the footer countdown.");
        _feedbackCountdownClock?.Controller?.SeekAlignedToLastTick(TimeSpan.FromSeconds(elapsedSeconds), TimeSeekOrigin.BeginTime);
    }
    private void UpdateHistoryButtons()
    {
        _undo.IsEnabled = _mixer.CanUndo; _redo.IsEnabled = _mixer.CanRedo;
        string undo = _mixer.UndoDescription, redo = _mixer.RedoDescription;
        _undo.ToolTip = _mixer.IsLayoutLocked && _mixer.EditHistory.UndoChangesLayout ? "Unlock layout to undo: " + undo : undo.Length > 0 ? "Undo: " + undo : "Nothing to undo";
        _redo.ToolTip = _mixer.IsLayoutLocked && _mixer.EditHistory.RedoChangesLayout ? "Unlock layout to redo: " + redo : redo.Length > 0 ? "Redo: " + redo : "Nothing to redo";
        AutomationProperties.SetName(_undo, undo.Length > 0 ? "Undo: " + undo : "Undo");
        AutomationProperties.SetName(_redo, redo.Length > 0 ? "Redo: " + redo : "Redo");
    }
    internal bool RestoreHistory(bool redo)
    {
        CancelActiveGesture();
        Mouse.Captured?.ReleaseMouseCapture();
        _mixer.EndEditGesture();
        string description = redo ? _mixer.RedoDescription : _mixer.UndoDescription;
        bool restored = redo ? _mixer.Redo() : _mixer.Undo();
        if (restored) SetMessage((redo ? "Redone: " : "Undone: ") + description + ".");
        else if (_mixer.IsLayoutLocked && (redo ? _mixer.EditHistory.RedoChangesLayout : _mixer.EditHistory.UndoChangesLayout)) SetMessage(MixerViewModel.LayoutLockedMessage);
        else if (!string.IsNullOrWhiteSpace(_mixer.Warning)) SetMessage(_mixer.Warning, isError: true);
        else SetMessage(_mixer.CanEdit ? (redo ? "Nothing to redo." : "Nothing to undo.") : "Reconnect to the audio engine before changing the graph.");
        Refresh();
        return restored;
    }
    internal ContextMenu CreateHistoryMenu()
    {
        var menu = TrackPracticeMenu(new ContextMenu { MaxHeight = 500, Width = 440 });
        menu.Items.Add(new MenuItem { Header = "Activity · this session", IsEnabled = false });
        menu.Items.Add(new Separator());
        if (_mixer.EditHistory.Entries.Count == 0) menu.Items.Add(new MenuItem { Header = "No edits yet", IsEnabled = false });
        foreach (var entry in _mixer.EditHistory.Entries)
            menu.Items.Add(new MenuItem { Header = entry.Description, InputGestureText = entry.Time.ToString("HH:mm:ss"), IsEnabled = false });
        if (_lastMessage.Length > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = new TextBlock { Text = _lastMessage, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 }, IsEnabled = false });
        }
        MenuHoverHelp.Attach(menu);
        return menu;
    }
    private void ShowHistoryMenu() { var menu = CreateHistoryMenu(); menu.PlacementTarget = _history; menu.IsOpen = true; }
    internal void SelectNode(string id)
    {
        SetNodeSelection([id]);
    }
    private void SetNodeSelection(IEnumerable<string> ids)
    {
        var next = ids.Where(id => Graph.Nodes.Any(node => node.Id == id)).ToHashSet();
        if (_selectedEdge is null && _selectedNodes.SetEquals(next)) return;
        _selectedNodes.Clear(); _selectedNodes.UnionWith(next); _selectedEdge = null;
        Refresh();
    }
    internal void SelectNodeForPointer(string id, ModifierKeys modifiers)
    {
        if ((modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0)
        {
            var next = _selectedNodes.ToHashSet();
            if (!next.Remove(id)) next.Add(id);
            SetNodeSelection(next);
        }
        else if (!_selectedNodes.Contains(id)) SelectNode(id);
    }
    private IEnumerable<string> NodesInRectangle(Point start, Point end)
    {
        var rectangle = new Rect(ToWorld(start), ToWorld(end));
        return _nodes.Where(pair => rectangle.IntersectsWith(new Rect(Canvas.GetLeft(pair.Value), Canvas.GetTop(pair.Value), pair.Value.ActualWidth, pair.Value.ActualHeight))).Select(pair => pair.Key);
    }
    internal void SelectRectangle(Point viewportStart, Point viewportEnd, bool additive)
    {
        SetNodeSelection((additive ? _selectedNodes : []).Concat(NodesInRectangle(viewportStart, viewportEnd)).ToArray());
    }
    internal void BeginBoxSelection(Point start, bool additive)
    {
        CancelCable(); _boxOriginalSelection = _selectedNodes.ToHashSet();
        if (!additive) SetNodeSelection([]);
        _boxSelecting = true; _gestureStart = start; _moved = false;
        _boxBaseSelection = additive ? _boxOriginalSelection.ToHashSet() : [];
        Canvas.SetLeft(_selectionBox, start.X); Canvas.SetTop(_selectionBox, start.Y);
        _selectionBox.Width = _selectionBox.Height = 0;
    }
    private HashSet<string> _boxBaseSelection = [];
    internal void UpdateBoxSelection(Point point)
    {
        if (!_boxSelecting) return;
        if ((point - _gestureStart).Length > 3) _moved = true;
        if (!_moved) return;
        var bounds = new Rect(_gestureStart, point);
        Canvas.SetLeft(_selectionBox, bounds.X); Canvas.SetTop(_selectionBox, bounds.Y);
        _selectionBox.Width = bounds.Width; _selectionBox.Height = bounds.Height; _selectionBox.Visibility = Visibility.Visible;
        SetNodeSelection(_boxBaseSelection.Concat(NodesInRectangle(_gestureStart, point)).ToArray());
    }
    internal void SelectEdge(string id)
    {
        _selectedEdge = id; _selectedNodes.Clear();
        if (PresentationSource.FromVisual(this) is not null) Focus();
        Refresh();
        var edge = Graph.Edges.FirstOrDefault(e => e.Id == id); if (edge is null) return;
        var details = DescribeConnection(Graph, edge);
        SetMessage($"{details.Route} · {details.Title} · {details.State} · " + (_mixer.IsLayoutLocked ? "Layout locked; sound controls remain available." : "Delete removes this cable."));
    }
    internal static (string Title, string Route, string State, string Purpose) DescribeConnection(GraphConfiguration graph, GraphEdge edge)
    {
        string from = graph.Nodes.FirstOrDefault(node => node.Id == edge.From)?.Name ?? "Missing source";
        string to = graph.Nodes.FirstOrDefault(node => node.Id == edge.To)?.Name ?? "Missing destination";
        string gain = (edge.GainDb > 0 ? "+" : "") + edge.GainDb.ToString("G", CultureInfo.InvariantCulture) + " dB";
        return edge.Kind switch
        {
            "control" => ("Linked control connection", $"{from} → {to}",
                $"Linked control {(edge.Muted ? "off" : "on")} · Saved offset {gain}", "Links level and mute; carries no audio"),
            "sidechain" => ("Sidechain / trigger connection", $"{from} → {to}",
                $"Detector {(edge.Muted ? "off" : "on")} · Saved level {gain}", "Detector only; adds no audible audio"),
            _ => ("Audio connection", $"{from} → {to}",
                $"Connection {(edge.Muted ? "off" : "on")} · Saved level {gain}", "")
        };
    }
    private void UpdateConnectionDetails(GraphEdge edge, WireVisual wire)
    {
        var key = new ConnectionDetailKey(edge.From, edge.To,
            Graph.Nodes.FirstOrDefault(node => node.Id == edge.From)?.Name ?? "Missing source",
            Graph.Nodes.FirstOrDefault(node => node.Id == edge.To)?.Name ?? "Missing destination",
            edge.Kind, edge.GainDb, edge.Muted);
        if (wire.DetailKey == key) return;
        bool routeChanged = wire.DetailKey?.SourceName != key.SourceName || wire.DetailKey?.TargetName != key.TargetName;
        wire.DetailKey = key;
        var details = DescribeConnection(Graph, edge);
        if (routeChanged) wire.Hit.ToolTip = NodeToolTip.Create(details.Route);
        AutomationProperties.SetName(wire.Hit, details.Route);
        AutomationProperties.SetHelpText(wire.Hit, $"{details.Title}. {details.State}. {details.Purpose}");
    }
    internal void BeginDrag(GraphNodeView node, MouseButtonEventArgs args)
    {
        if (!_mixer.CanEdit || args.ChangedButton != MouseButton.Left) return;
        Focus(); args.Handled = true;
        if (BeginNodePointerGesture(node.Id, args.GetPosition(_viewport), Keyboard.Modifiers)
            && !_viewport.CaptureMouse()) FinishGesture(cancel: true);
    }
    internal bool BeginNodePointerGesture(string id, Point viewportStart, ModifierKeys modifiers)
    {
        if (!_mixer.CanEdit || !_nodes.ContainsKey(id)) return false;
        SelectNodeForPointer(id, modifiers);
        if (!_selectedNodes.Contains(id)) return false;
        // A header click is ordinary selection, even while locked. Wait for
        // movement before reporting a refused edit; never move its visual.
        if (_mixer.IsLayoutLocked) BeginLockedLayoutGesture(viewportStart);
        else BeginNodeMove(id, viewportStart, modifiers.HasFlag(ModifierKeys.Alt));
        return true;
    }
    internal void BeginLockedLayoutGesture(Point viewportStart)
    {
        if (!_mixer.IsLayoutLocked) return;
        _lockedLayoutGestureStart = viewportStart;
        _lockedLayoutGestureNotified = false;
    }
    internal bool UpdateLockedLayoutGesture(Point viewportPoint)
    {
        if (_lockedLayoutGestureStart is not Point start) return false;
        if (!_lockedLayoutGestureNotified && (viewportPoint - start).Length > 3 && _mixer.IsLayoutLocked)
        {
            _lockedLayoutGestureNotified = true;
            RequireLayoutEdit();
        }
        return true;
    }
    internal void BeginNodeMove(string id, Point viewportStart, bool detach = false)
    {
        if (!_nodes.TryGetValue(id, out var node) || !RequireLayoutEdit()) return;
        if (!_selectedNodes.Contains(id)) SelectNode(id);
        CancelCable(); _dragged = node; _gestureStart = viewportStart; _moved = false; _viewport.Cursor = Cursors.SizeAll;
        _detachOnDrop = detach; SetInsertionEdge(null);
        if (detach) SetMessage(_selectedNodes.Count == 1 ? "Release to detach this node and keep its audio path connected. Escape cancels." : "Move the group normally. Detach works on one node at a time.");
        _dragOrigins.Clear();
        foreach (string selected in _selectedNodes)
            if (_nodes.TryGetValue(selected, out var view))
            { _dragOrigins.Add(selected, new Point(Canvas.GetLeft(view), Canvas.GetTop(view))); Panel.SetZIndex(view, 2); }
    }
    private Vector ClampGroupDelta(Vector delta)
    {
        if (_dragOrigins.Count == 0) return new Vector();
        return new Vector(Math.Clamp(delta.X, -100000 - _dragOrigins.Values.Min(p => p.X), 100000 - _dragOrigins.Values.Max(p => p.X)),
            Math.Clamp(delta.Y, -100000 - _dragOrigins.Values.Min(p => p.Y), 100000 - _dragOrigins.Values.Max(p => p.Y)));
    }
    internal void UpdateNodeMove(Point viewportPoint)
    {
        if (_dragged is null || _dragOrigins.Count == 0) return;
        Vector pixels = viewportPoint - _gestureStart;
        if (pixels.Length > 3) _moved = true;
        if (!_moved) return;
        Vector delta = ClampGroupDelta(pixels / _zoom);
        foreach (var (id, origin) in _dragOrigins)
            if (_nodes.TryGetValue(id, out var view)) { Canvas.SetLeft(view, origin.X + delta.X); Canvas.SetTop(view, origin.Y + delta.Y); }
        UpdateWires();
        SetInsertionEdge(!_detachOnDrop && _dragOrigins.Count == 1 ? FindInsertionCandidate(_dragged.Id) : null);
    }
    internal void FinishNodeMove(bool cancel = false)
    {
        cancel |= !_mixer.CanEditLayout;
        var dragged = _dragged; _dragged = null;
        if (dragged is null) return;
        _viewport.Cursor = null;
        // Commit exactly the cable shown while dragging. Grid rounding may
        // move the node nearer another cable; that must not change the route.
        string? previewedInsertion = _insertionEdge;
        var positions = new Dictionary<string, Point>();
        Vector delta = _dragOrigins.TryGetValue(dragged.Id, out var first) ? new Point(Canvas.GetLeft(dragged), Canvas.GetTop(dragged)) - first : new Vector();
        delta = cancel || !_moved ? new Vector() : ClampGroupDelta(new Vector(Math.Round(delta.X / 8) * 8, Math.Round(delta.Y / 8) * 8));
        foreach (var (id, origin) in _dragOrigins)
        {
            positions[id] = origin + delta;
            if (_nodes.TryGetValue(id, out var view)) { Panel.SetZIndex(view, 0); Canvas.SetLeft(view, positions[id].X); Canvas.SetTop(view, positions[id].Y); }
        }
        GraphConnectionEditing.Plan? connectionPlan = null;
        string connectionMessage = "";
        if (!cancel && delta.LengthSquared > 0 && _mixer.CanEdit)
        {
            if (_detachOnDrop && positions.Count == 1)
            {
                if (GraphConnectionEditing.TryPrepareDetach(Graph, dragged.Id, out connectionPlan, out string error)) connectionMessage = "Node detached; its audio path stays connected.";
                else { connectionPlan = null; connectionMessage = error; }
            }
            else if (!_detachOnDrop && positions.Count == 1 && previewedInsertion is string insertion)
            {
                if (GraphConnectionEditing.TryPrepareInsertion(Graph, dragged.Id, insertion, out connectionPlan, out string error)) connectionMessage = "Node inserted into the audio cable.";
                else { connectionPlan = null; connectionMessage = error; }
            }
        }
        _dragOrigins.Clear(); _detachOnDrop = false; SetInsertionEdge(null);
        if (connectionMessage.Length > 0) SetMessage(connectionMessage);
        if (!cancel && delta.LengthSquared > 0 && _mixer.CanEdit)
            _mixer.Edit(c =>
            {
                foreach (var node in c.Graph.Nodes) if (positions.TryGetValue(node.Id, out var position)) { node.X = position.X; node.Y = position.Y; }
                if (connectionPlan is not null && !GraphConnectionEditing.TryApply(c.Graph, connectionPlan, out string error)) SetMessage(error, isError: true);
            });
        else Refresh();
        UpdateWires();
    }
    internal void NodeChanged(string id, Action<GraphNode> change)
    {
        _mixer.Edit(c => { var node = c.Graph.Nodes.FirstOrDefault(n => n.Id == id); if (node is not null) change(node); });
    }
    internal void EdgeChanged(string id, Action<GraphEdge> change)
    {
        _mixer.Edit(c => { var edge = c.Graph.Edges.FirstOrDefault(e => e.Id == id); if (edge is not null) change(edge); });
    }
    private bool RequireLayoutEdit()
    {
        if (_mixer.IsLayoutLocked) { SetMessage(MixerViewModel.LayoutLockedMessage); _mixer.NotifyLayoutEditBlocked(); return false; }
        return _mixer.CanEdit;
    }
    internal void RemoveEdge(string id) { if (!Graph.Edges.Any(edge => edge.Id == id) || !RequireLayoutEdit()) return; _mixer.Edit(c => c.Graph.Edges.RemoveAll(e => e.Id == id)); SetMessage("Connection removed."); }
    internal void SocketPressed(Socket socket, MouseButtonEventArgs args)
    {
        if (!_mixer.CanEdit || args.ChangedButton != MouseButton.Left) return;
        Focus(); args.Handled = true;
        if (!RequireLayoutEdit()) return;
        if (_pendingSocket is not null && _pendingSocket != socket) { Connect(_pendingSocket, socket); return; }
        if (!socket.Output)
        {
            if (BeginInputLinkDrag(socket, args.GetPosition(_viewport))) _viewport.CaptureMouse();
            return;
        }
        SelectNode(socket.NodeId);
        BeginConnectionPreview(socket, args.GetPosition(_viewport), dragging: true); _viewport.CaptureMouse();
        SetMessage(socket.Output ? "Choose a compatible input socket. Escape cancels." : "Choose a compatible output socket. Escape cancels."); UpdatePreview();
    }
    private void WirePressed(string id, MouseButtonEventArgs args)
    {
        SelectEdge(id); args.Handled = true;
        if (!_mixer.CanEdit || args.ChangedButton != MouseButton.Left) return;
        var edge = Graph.Edges.FirstOrDefault(e => e.Id == id);
        if (edge is null || !_nodes.TryGetValue(edge.To, out var node) || node.FindSocket(edge.Kind, false) is not Socket input) return;
        Point endpoint = SocketPosition(input), pointer = args.GetPosition(_viewport);
        if (_mixer.IsLayoutLocked)
        {
            BeginLockedLayoutGesture(pointer);
            if (!_viewport.CaptureMouse()) FinishGesture(cancel: true);
            return;
        }
        if ((new Point(endpoint.X * _zoom + _panX, endpoint.Y * _zoom + _panY) - pointer).Length > 64) return;
        if (BeginInputLinkDrag(input, pointer, id)) _viewport.CaptureMouse();
    }
    internal bool BeginInputLinkDrag(Socket input, Point viewportStart, string? edgeId = null)
    {
        if (input.Output || !IsCurrentSocket(input) || !RequireLayoutEdit()) return false;
        var incoming = Graph.Edges.Where(e => e.To == input.NodeId && e.Kind == input.Kind).ToArray();
        string? selected = edgeId ?? _selectedEdge;
        GraphEdge? edge = incoming.FirstOrDefault(e => e.Id == selected);
        CancelCable();
        if (incoming.Length == 0)
        {
            SelectNode(input.NodeId); BeginConnectionPreview(input, viewportStart, dragging: true);
            SetMessage("Choose a compatible output socket. Escape cancels."); return true;
        }
        if (edge is null && incoming.Length == 1) edge = incoming[0];
        _gestureStart = viewportStart; _pointer = ToWorld(viewportStart); _socketDragging = true; _moved = false;
        if (edge is not null) return BeginExistingLinkPreview(edge, input);
        // These audio sockets accept several cables at one shared anchor.
        // Follow a distinguishable curve rather than choosing by list order.
        SelectNode(input.NodeId); _inputLinkSocket = input; _inputLinkCandidates = incoming.Select(e => e.Copy()).ToArray();
        _pendingSocket = input; SetSnapSocket(input); ClearPreview();
        SetMessage("Follow the cable you want away from this input, or select that cable first. Escape cancels.");
        return true;
    }
    private bool BeginExistingLinkPreview(GraphEdge edge, Socket input)
    {
        if (!_nodes.TryGetValue(edge.From, out var source) || source.FindSocket(edge.Kind == "sidechain" ? "audio" : edge.Kind, true) is not Socket output) { CancelCable(); return false; }
        _rewireOriginal = edge.Copy(); _pendingSocket = output; _inputLinkSocket = null; _inputLinkCandidates = [];
        SetInputLinkCandidate(null); _selectedEdge = edge.Id; _selectedNodes.Clear(); SetSnapSocket(input); Refresh();
        SetMessage("Move this cable to another input, or release on empty canvas to remove it. Escape cancels.");
        return true;
    }
    private void ChooseIncomingLink(Point viewportPoint)
    {
        if (_inputLinkSocket is not Socket input) return;
        var distances = _inputLinkCandidates.Where(e => _wires.ContainsKey(e.Id))
            .Select(e => (Edge: e, Distance: GraphConnectionEditing.DistanceToPolyline(_wires[e.Id].Points, ToWorld(viewportPoint)) * _zoom))
            .OrderBy(item => item.Distance).ToArray();
        var closest = distances.FirstOrDefault();
        bool unique = closest.Edge is not null && closest.Distance <= 12
            && (distances.Length == 1 || distances[1].Distance - closest.Distance >= 1.5);
        SetInputLinkCandidate(unique ? closest.Edge!.Id : null);
        if (!unique) { SetMessage("Follow one incoming cable. If they overlap, select the cable first, then drag this input."); return; }
        Point anchor = SocketPosition(input);
        if ((new Point(anchor.X * _zoom + _panX, anchor.Y * _zoom + _panY) - viewportPoint).Length < 12) return;
        // Once the pointer identifies a branch outside the socket, lock its
        // exact snapshot for the rest of the gesture and for replay checking.
        if (BeginExistingLinkPreview(closest.Edge!, input)) SetSnapSocket(FindSnapSocket(viewportPoint, _pendingSocket));
    }
    private void Connect(Socket first, Socket second)
    {
        if (!CompatibleSockets(first, second)) { SetMessage("Connect an audio output to Audio or Sidechain, or link matching control sockets.", isError: true); return; }
        Socket from = first.Output ? first : second, to = first.Output ? second : first;
        string error = ConnectNodes(from.NodeId, to.NodeId, to.Kind);
        if (error.Length > 0) { SetMessage(error, isError: true); CancelCable(); return; }
        CancelCable(); SetMessage(to.Kind == "control" ? "Linked control connected. Its level now applies to that node." : to.Kind == "sidechain" ? "Ducking trigger connected. This cable controls reduction and adds no audible audio." : "Audio connected.");
    }
    private static bool CompatibleSockets(Socket first, Socket second)
    {
        if (first.Output == second.Output) return false;
        var from = first.Output ? first : second; var to = first.Output ? second : first;
        return from.Kind == "control" ? to.Kind == "control" : from.Kind == "audio" && to.Kind is "audio" or "sidechain";
    }
    public string ConnectNodes(string from, string to, string kind = "audio")
    {
        if (_mixer.IsLayoutLocked) { RequireLayoutEdit(); return MixerViewModel.LayoutLockedMessage; }
        if (!_mixer.CanEdit) return "Wait for the audio engine before editing connections.";
        var edge = new GraphEdge { Id = Guid.NewGuid().ToString("N"), From = from, To = to, Kind = kind };
        string error = GraphDefaults.ConnectionError(Graph, edge); if (error.Length > 0) return error;
        _mixer.Edit(c => { if (GraphDefaults.ConnectionError(c.Graph, edge).Length == 0) c.Graph.Edges.Add(edge.Copy()); }); return "";
    }
    public void RemoveNode(string id)
    {
        if (!Graph.Nodes.Any(node => node.Id == id) || !RequireLayoutEdit()) return;
        CancelCable();
        _mixer.Edit(c => { c.Graph.Nodes.RemoveAll(n => n.Id == id); c.Graph.Edges.RemoveAll(e => e.From == id || e.To == id); });
        _selectedNodes.Remove(id); CancelCable(); SetMessage("Node and its connections removed."); Refresh();
    }
    private void CancelCable()
    {
        string? old = _rewireOriginal?.Id;
        _rewireOriginal = null; _inputLinkSocket = null; _inputLinkCandidates = []; SetInputLinkCandidate(null);
        _pendingSocket = null; _socketDragging = false; SetSnapSocket(null); ClearPreview();
        if (old is not null) RefreshWireAppearance(old);
        if (_dragged is null && _lockedLayoutGestureStart is null && !HasCanvasPanGesture && !_boxSelecting && !_cutting && Mouse.Captured == _viewport) _viewport.ReleaseMouseCapture();
    }
    private void CanvasPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (HasCanvasPanGesture) { args.Handled = true; return; }
        if (args.Handled || !_mixer.CanEdit || Keyboard.IsKeyDown(Key.Space) || _dragged is not null || _boxSelecting || _cutting) return;
        var source = args.OriginalSource as DependencyObject;
        if (FindParent<Socket>(source) is not null) return; // The socket's own handler remains authoritative.
        if (_pendingSocket is null && (Keyboard.Modifiers != ModifierKeys.None || IsEditingControl(source) || FindParent<ButtonBase>(source) is not null)) return;
        var target = FindSnapSocket(args.GetPosition(_viewport), _pendingSocket);
        if (target is not null) SocketPressed(target, args);
    }
    private void CanvasPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs args)
    {
        if (HasCanvasPanGesture) { args.Handled = true; return; }
        _suppressContextMenu = false;
        if (!_mixer.CanEdit || !Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (!RequireLayoutEdit()) { _suppressContextMenu = true; args.Handled = true; return; }
        BeginCableCut(args.GetPosition(_viewport)); _viewport.CaptureMouse(); args.Handled = true;
    }
    private void CanvasPreviewMouseRightButtonUp(object sender, MouseButtonEventArgs args)
    {
        if (!_suppressContextMenu) return;
        // A cancelled drag may have released capture over a node. Consume its
        // final release before that node's menu can open or change selection.
        if (_cutting || HasCanvasPanGesture) CanvasMouseUp(sender, args);
        args.Handled = true;
    }
    private void CanvasMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (args.Handled) return;
        if (HasCanvasPanGesture) { args.Handled = true; return; }
        if (args.ChangedButton == MouseButton.Middle || args.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space))
        { BeginCanvasPan(args.ChangedButton, args.GetPosition(_viewport)); Focus(); if (!_viewport.CaptureMouse()) FinishGesture(cancel: true); args.Handled = true; return; }
        if (FindParent<GraphNodeView>(args.OriginalSource as DependencyObject) is not null) return;
        if (args.ChangedButton == MouseButton.Right)
        {
            if (TryBeginRightCanvasGesture(args.GetPosition(_viewport), args.OriginalSource as DependencyObject, Keyboard.Modifiers))
            { Focus(); if (!_viewport.CaptureMouse()) FinishGesture(cancel: true); }
            else if (_dragged is null && !_socketDragging && !_boxSelecting && !_cutting) ShowCanvasMenu(ToWorld(args.GetPosition(_viewport)));
            args.Handled = true;
        }
        else if (args.ChangedButton == MouseButton.Left) { BeginBoxSelection(args.GetPosition(_viewport), (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0); Focus(); _viewport.CaptureMouse(); args.Handled = true; }
    }
    private void CanvasMouseMove(object sender, MouseEventArgs args)
    {
        Point point = args.GetPosition(_viewport); _pointer = ToWorld(point);
        if (UpdateLockedLayoutGesture(point)) { args.Handled = true; return; }
        if (_cutting) { UpdateCableCut(point); return; }
        if (HasCanvasPanGesture) { UpdateCanvasPan(point); args.Handled = true; return; }
        if (_pendingSocket is not null) UpdateConnectionPointer(point);
        else if (_dragged is null && !_boxSelecting)
        {
            var source = args.OriginalSource as DependencyObject;
            SetSnapSocket(_mixer.CanEditLayout && (_pendingSocket is not null || !IsEditingControl(source) && (FindParent<ButtonBase>(source) is null || FindParent<Socket>(source) is not null))
                ? FindSnapSocket(point, _pendingSocket) : null);
        }
        Vector delta = point - _gestureStart;
        if (delta.Length > 3) _moved = true;
        if (_dragged is not null) UpdateNodeMove(point);
        else if (_boxSelecting) UpdateBoxSelection(point);
    }
    private void CanvasMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (_cutting) { if (args.ChangedButton == MouseButton.Right) { UpdateCableCut(args.GetPosition(_viewport)); FinishCableCut(); } args.Handled = true; return; }
        Point point = args.GetPosition(_viewport);
        if (_lockedLayoutGestureStart is not null)
        {
            if (args.ChangedButton == MouseButton.Left) { UpdateLockedLayoutGesture(point); FinishGesture(); }
            args.Handled = true; return;
        }
        if (FinishCanvasPan(args.ChangedButton, point, out var menuPosition))
        {
            args.Handled = true;
            if (menuPosition is Point position) ShowCanvasMenu(position);
            return;
        }
        var hit = _viewport.InputHitTest(point) as DependencyObject;
        bool blank = new Rect(_viewport.RenderSize).Contains(point) && FindParent<GraphNodeView>(hit) is null && !IsEditingControl(hit)
            && FindParent<ButtonBase>(hit) is null && FindParent<System.Windows.Shapes.Path>(hit) is null && FindSnapSocket(point, null) is null;
        if (FinishConnectionPointer(args.ChangedButton, point, blank)) { args.Handled = true; return; }
        if (args.ChangedButton is MouseButton.Left or MouseButton.Middle) FinishGesture();
    }
    internal bool TryBeginRightCanvasGesture(Point point, DependencyObject? source, ModifierKeys modifiers)
    {
        if (!_mixer.RightClickDragToPan || modifiers.HasFlag(ModifierKeys.Control) || HasCanvasPanGesture
            || _dragged is not null || _socketDragging || _boxSelecting || _cutting
            || FindParent<GraphNodeView>(source) is not null || FindParent<System.Windows.Shapes.Path>(source) is not null
            || IsEditingControl(source) || FindParent<ButtonBase>(source) is not null) return false;
        BeginCanvasPan(MouseButton.Right, point);
        return true;
    }
    internal void BeginCanvasPan(MouseButton button, Point point)
    {
        CancelActiveGesture();
        _panButton = button; _panning = button != MouseButton.Right;
        _gestureStart = point; _originalPosition = new(_panX, _panY);
        if (button == MouseButton.Right) _suppressContextMenu = true;
        _viewport.Cursor = _panning ? Cursors.ScrollAll : null;
    }
    internal void UpdateCanvasPan(Point point)
    {
        if (!HasCanvasPanGesture) return;
        Vector delta = point - _gestureStart;
        if (!_panning && delta.Length < RightPanThreshold) return;
        _panning = true; _viewport.Cursor = Cursors.ScrollAll;
        _panX = _originalPosition.X + delta.X; _panY = _originalPosition.Y + delta.Y; ApplyTransform();
    }
    internal bool FinishCanvasPan(MouseButton button, Point point, out Point? menuPosition)
    {
        menuPosition = null;
        if (_panButton is not MouseButton initiatingButton) return false;
        if (button != initiatingButton) return true;
        // Include the final position even when no MouseMove arrived before release.
        UpdateCanvasPan(point);
        if (button == MouseButton.Right && !_panning && new Rect(_viewport.RenderSize).Contains(point))
            menuPosition = ToWorld(_gestureStart);
        FinishGesture();
        return true;
    }
    internal void BeginConnectionPreview(Socket socket, Point viewportStart, bool dragging = false)
    {
        if (!RequireLayoutEdit()) return;
        CancelCable();
        _pendingSocket = socket; _gestureStart = viewportStart; _pointer = ToWorld(viewportStart); _socketDragging = dragging; _moved = false;
        SetSnapSocket(null); UpdatePreview();
    }
    internal void UpdateConnectionPointer(Point viewportPoint)
    {
        if (_pendingSocket is null) return;
        _pointer = ToWorld(viewportPoint);
        if ((viewportPoint - _gestureStart).Length > 3) _moved = true;
        if (_inputLinkSocket is not null) { ChooseIncomingLink(viewportPoint); UpdatePreview(); return; }
        SetSnapSocket(FindSnapSocket(viewportPoint, _pendingSocket)); UpdatePreview();
    }
    internal bool FinishConnectionPointer(MouseButton button, Point viewportPoint, bool blankCanvas = true)
    {
        if (_mixer.IsLayoutLocked) { bool active = _pendingSocket is not null; CancelCable(); return active; }
        if (!_socketDragging || _pendingSocket is null) return false;
        if (button != MouseButton.Left) return true;
        _socketDragging = false;
        if (Mouse.Captured == _viewport) _viewport.ReleaseMouseCapture();
        if (_inputLinkSocket is not null) { CancelCable(); SetMessage("No cable moved. Select the cable you want, then drag its input end."); return true; }
        if (_rewireOriginal is GraphEdge original)
        {
            if (!_moved) { CancelCable(); SetMessage("Cable selected. Drag its input end to move or remove it."); return true; }
            var target = FindSnapSocket(viewportPoint, _pendingSocket);
            GraphConnectionEditing.EdgePlan? plan = null; string error = "";
            bool prepared = target is not null
                ? target.NodeId != original.To && GraphConnectionEditing.TryPrepareRewire(Graph, original, target.NodeId, out plan, out error)
                : blankCanvas && GraphConnectionEditing.TryPrepareRemoval(Graph, [original], out plan, out error);
            CancelCable();
            if (prepared)
            {
                SetMessage(target is null ? "Cable removed." : "Cable moved.");
                _mixer.Edit(c => { if (!GraphConnectionEditing.TryApply(c.Graph, plan!, out string applyError)) SetMessage(applyError); });
            }
            else SetMessage(error.Length > 0 ? error : "Original cable kept.", isError: error.Length > 0);
            return true;
        }
        if (_moved)
        {
            var target = FindSnapSocket(viewportPoint, _pendingSocket);
            if (target is not null && target != _pendingSocket) Connect(_pendingSocket, target);
            else { CancelCable(); SetMessage("Connection cancelled. Drag between matching sockets, or click them one at a time."); }
        }
        return true;
    }
    internal void HandleCaptureLoss()
    {
        if (_cutting) FinishCableCut(cancel: true);
        if (_socketDragging) CancelCable();
        if (_dragged is not null || HasCanvasPanGesture || _boxSelecting || _lockedLayoutGestureStart is not null) FinishGesture(cancel: true);
    }
    internal void CancelActiveGesture()
    {
        if (_cutting) FinishCableCut(cancel: true);
        CancelCable(); FinishGesture(cancel: true);
    }
    internal void FinishGesture(bool cancel = false)
    {
        _lockedLayoutGestureStart = null; _lockedLayoutGestureNotified = false;
        bool panned = _panning; _panning = false; _panButton = null; _viewport.Cursor = null;
        FinishNodeMove(cancel);
        if (_boxSelecting) { _boxSelecting = false; _selectionBox.Visibility = Visibility.Collapsed; if (cancel) SetNodeSelection(_boxOriginalSelection); }
        if (panned) { if (cancel) { _panX = _originalPosition.X; _panY = _originalPosition.Y; ApplyTransform(); } else PersistView(); }
        if (Mouse.Captured == _viewport && !_socketDragging) _viewport.ReleaseMouseCapture();
    }
    internal void BeginCableCut(Point point)
    {
        if (!RequireLayoutEdit()) return;
        CancelActiveGesture(); Focus(); _cutting = true; _suppressContextMenu = true;
        _cutCandidates.Clear(); _cutEdges.Clear();
        foreach (var edge in Graph.Edges) _cutCandidates[edge.Id] = edge.Copy();
        _lastCutPoint = point; _cutStroke.Points.Clear(); _cutStroke.Points.Add(point);
        _cutStroke.SetResourceReference(Shape.StrokeProperty, "NodeSelectedBrush");
        _cutStroke.Visibility = Visibility.Visible;
        if (!_selectionLayer.Children.Contains(_cutStroke)) _selectionLayer.Children.Add(_cutStroke);
        _viewport.Cursor = Cursors.Cross;
    }
    internal void UpdateCableCut(Point point)
    {
        if (!_cutting || (point - _lastCutPoint).Length < 2) return;
        foreach (var edge in _cutCandidates.Values)
            if (!_cutEdges.ContainsKey(edge.Id) && _wires.TryGetValue(edge.Id, out var wire)
                && GraphConnectionEditing.PolylineIntersectsStroke(wire.Points, ToWorld(_lastCutPoint), ToWorld(point), 3 / _zoom))
            { _cutEdges[edge.Id] = edge; RefreshWireAppearance(edge.Id); }
        _lastCutPoint = point; _cutStroke.Points.Add(point);
    }
    internal void FinishCableCut(bool cancel = false)
    {
        if (!_cutting) return;
        var originals = _cutEdges.Values.ToArray(); _cutting = false;
        _cutEdges.Clear(); _cutCandidates.Clear(); _cutStroke.Points.Clear(); _cutStroke.Visibility = Visibility.Collapsed; _viewport.Cursor = null;
        foreach (var edge in originals) RefreshWireAppearance(edge.Id);
        if (Mouse.Captured == _viewport) _viewport.ReleaseMouseCapture();
        if (cancel || originals.Length == 0 || !_mixer.CanEditLayout) return;
        if (!GraphConnectionEditing.TryPrepareRemoval(Graph, originals, out var plan, out string error)) { SetMessage(error, isError: true); return; }
        _mixer.Edit(c => { if (!GraphConnectionEditing.TryApply(c.Graph, plan!, out string message)) SetMessage(message); });
        SetMessage($"Removed {originals.Length} connection{(originals.Length == 1 ? "" : "s")}.");
    }
    private void SetInputLinkCandidate(string? id)
    {
        string? old = _inputLinkCandidate; _inputLinkCandidate = id;
        if (old is not null) RefreshWireAppearance(old);
        if (id is not null) RefreshWireAppearance(id);
    }
    private void RefreshWireAppearance(string id)
    {
        if (!_wires.TryGetValue(id, out var wire)) return;
        var edge = Graph.Edges.FirstOrDefault(e => e.Id == id);
        if (edge is not null) UpdateWireAppearance(edge, wire);
    }
    private void UpdateWireAppearance(GraphEdge edge, WireVisual wire)
    {
        string id = edge.Id;
        bool highlighted = id == _selectedEdge || id == _insertionEdge || id == _inputLinkCandidate || _cutEdges.ContainsKey(id);
        string brushKey = highlighted ? "NodeSelectedBrush" : edge.Muted ? "ConsoleMutedBrush" : edge.Kind == "control" ? "NodeControlBrush" : edge.Kind == "sidechain" ? "NodeSidechainBrush" : "NodeAudioBrush";
        if (wire.BrushKey != brushKey) { wire.BrushKey = brushKey; wire.Visible.SetResourceReference(Shape.StrokeProperty, brushKey); }
        wire.Visible.Opacity = highlighted ? 1 : edge.Muted ? .4 : edge.Kind == "control" ? .75 : .95;
        var visibility = _rewireOriginal?.Id == id ? Visibility.Hidden : Visibility.Visible;
        wire.Visible.Visibility = wire.Hit.Visibility = wire.Outline.Visibility = visibility;
        wire.Visible.StrokeDashArray = edge.Muted ? MutedWireDashes : null;
        SetWireWidth(id, wire);
    }
    private void WorkspaceKeyDown(object sender, KeyEventArgs args)
    {
        // Alt chords arrive as Key.System in WPF. Normalize only for alignment;
        // the existing canvas shortcuts keep their original key semantics.
        Key alignmentKey = args.Key == Key.System ? args.SystemKey : args.Key;
        if (TryNodeAlignmentShortcut(alignmentKey, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject))
        { args.Handled = true; return; }
        if (TryFrameSelectionShortcut(args.Key, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject))
        { args.Handled = true; return; }
        if (TryHistoryShortcut(args.Key, Keyboard.Modifiers, Keyboard.FocusedElement as DependencyObject))
        { args.Handled = true; return; }
        if (IsEditingControl(Keyboard.FocusedElement as DependencyObject)) return;
        if (args.Key == Key.Escape) { CancelActiveGesture(); SetMessage("Gesture cancelled."); args.Handled = true; return; }
        if (_dragged is not null || _lockedLayoutGestureStart is not null || HasCanvasPanGesture || _boxSelecting || _cutting || _socketDragging) return;
        if (args.Key is Key.Delete or Key.Back) { DeleteSelection(); args.Handled = true; }
        else if (args.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopyToClipboard(); args.Handled = true; }
        else if (args.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control) { PasteFromClipboard(); args.Handled = true; }
        else if (args.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control) { SetNodeSelection(Graph.Nodes.Select(n => n.Id)); args.Handled = true; }
        else if (args.Key == Key.Home) { FitAll(); args.Handled = true; }
        else if ((args.Key is Key.D0 or Key.NumPad0) && Keyboard.Modifiers == ModifierKeys.Control) { ResetZoom(); args.Handled = true; }
        else if (args.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { ShowAddMenu(_add, VisibleCenter()); args.Handled = true; }
        else if (args.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Shift) { ShowAddMenu(_add, VisibleCenter()); args.Handled = true; }
        else if (args.Key == Key.F3) { ShowAddMenu(_add, VisibleCenter()); args.Handled = true; }
    }
    internal bool TryHistoryShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused)
    {
        // Text fields retain their own local text undo, including uncommitted
        // numeric values. Graph history is available from all other controls.
        if (IsTextEditor(focused)) return false;
        bool undo = key == Key.Z && modifiers == ModifierKeys.Control;
        bool redo = key == Key.Y && modifiers == ModifierKeys.Control
            || key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);
        if (!undo && !redo) return false;
        RestoreHistory(redo); return true;
    }
    internal static bool IsTextEditor(DependencyObject? element)
        => FindParent<TextBoxBase>(element) is not null || FindParent<PasswordBox>(element) is not null;
    internal static bool IsEditingControl(DependencyObject? element)
        => IsTextEditor(element)
        || FindParent<ComboBox>(element) is not null || FindParent<Slider>(element) is not null
        || FindParent<EqualizerEditor>(element) is not null || FindParent<RotaryValueControl>(element) is not null
        || FindParent<VoiceColorPad>(element) is not null;
    internal void DeleteSelection()
    {
        if (_selectedEdge is null && _selectedNodes.Count == 0) return;
        if (!RequireLayoutEdit()) return;
        FinishGesture(cancel: true);
        CancelCable();
        if (_selectedEdge is string edge) { RemoveEdge(edge); _selectedEdge = null; }
        else if (_selectedNodes.Count > 0)
        {
            var ids = _selectedNodes.ToHashSet(); _selectedNodes.Clear();
            _mixer.Edit(c => { c.Graph.Nodes.RemoveAll(n => ids.Contains(n.Id)); c.Graph.Edges.RemoveAll(e => ids.Contains(e.From) || ids.Contains(e.To)); });
            SetMessage($"{ids.Count} {(ids.Count == 1 ? "node" : "nodes")} and their connections removed.");
        }
        CancelCable(); Refresh();
    }
    internal string CopySelection() => GraphClipboard.Serialize(Graph, _selectedNodes);
    internal bool PasteSelection(string payload, Point worldAnchor)
    {
        if (_mixer.IsLayoutLocked) { RequireLayoutEdit(); return false; }
        if (!_mixer.CanEdit) { SetMessage("Wait for the audio engine before pasting nodes.", isError: true); return false; }
        if (!GraphClipboard.TryCreatePaste(payload, Graph, worldAnchor, out var paste, out string error)) { SetMessage(error, isError: true); return false; }
        return ApplyPaste(paste, $"Pasted {paste.Nodes.Count} {(paste.Nodes.Count == 1 ? "node" : "nodes")}." + (paste.ClearedAssignments > 0 ? $" Choose a device for {paste.ClearedAssignments} copied {(paste.ClearedAssignments == 1 ? "output" : "outputs")}; the originals keep their assignments." : " Drag a selected header to position the group."));
    }
    private bool ApplyPaste(GraphPaste paste, string message)
    {
        FinishGesture(cancel: true); CancelCable();
        // Keep immutable copies for replay when a concurrent engine revision
        // requires rebasing pending edits. Never append an already-applied ID.
        if (!_mixer.TryEdit((MixerConfiguration c, out string error) => GraphClipboard.TryAppend(c.Graph, paste, out error), out string appendError))
        { SetMessage("Paste could not be applied: " + appendError, isError: true); return false; }
        SetNodeSelection(paste.Nodes.Select(node => node.Id));
        SetMessage(message);
        return true;
    }
    internal bool DuplicateInput(string id)
    {
        if (_mixer.IsLayoutLocked) { RequireLayoutEdit(); return false; }
        if (!_mixer.CanEdit) { SetMessage("Wait for the audio engine before duplicating an input.", isError: true); return false; }
        var node = Graph.Nodes.FirstOrDefault(n => n.Id == id);
        if (node is null || node.Kind is not ("microphone" or "virtualInput" or "appInput")) return false;
        var position = new Point(node.X + 384, node.Y + 32);
        while (Graph.Nodes.Any(n => Math.Abs(n.X - position.X) < 16 && Math.Abs(n.Y - position.Y) < 16))
            position += new Vector(32, 40);
        if (!GraphClipboard.TryCreatePaste(GraphClipboard.Serialize(Graph, [id]), Graph,
            position, out var paste, out string error))
        { SetMessage(error, isError: true); return false; }
        string copyName;
        int number = 1;
        do
        {
            string suffix = number == 1 ? " copy" : " copy " + number;
            int bytes = 0, characters = 0;
            foreach (var rune in node.Name.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 256 - suffix.Length) break;
                bytes += rune.Utf8SequenceLength; characters += rune.Utf16SequenceLength;
            }
            copyName = node.Name[..characters] + suffix;
            number++;
        } while (Graph.Nodes.Any(n => string.Equals(n.Name, copyName, StringComparison.OrdinalIgnoreCase)));
        paste.Nodes[0].Name = copyName;
        return ApplyPaste(paste, "Input duplicated with its own controls. Connect the new node to build another branch.");
    }
    private void CopyToClipboard()
    {
        if (_selectedNodes.Count == 0) { SetMessage("Select one or more nodes to copy."); return; }
        try
        {
            string payload = CopySelection();
            if (_useSystemClipboard)
            {
                var data = new DataObject(); data.SetData(GraphClipboard.FormatName, payload, autoConvert: false);
                Clipboard.SetDataObject(data, true);
            }
            else _practiceClipboard = payload;
            _lastPastedPayload = null; _pasteRepeat = 0;
            SetMessage($"Copied {_selectedNodes.Count} {(_selectedNodes.Count == 1 ? "node" : "nodes")} and their internal connections.");
        }
        catch (Exception error) when (error is ExternalException or System.Security.SecurityException)
        { SetMessage("The clipboard is busy. Try Copy again.", isError: true); }
    }
    private void PasteFromClipboard(Point? anchor = null)
    {
        try
        {
            string? payload = _practiceClipboard;
            if (_useSystemClipboard) Clipboard.TryGetData<string>(GraphClipboard.FormatName, out payload);
            if (payload is null) { SetMessage("Copy nodes in VineMix before pasting."); return; }
            int repeat = payload == _lastPastedPayload ? _pasteRepeat : 0;
            Point target = anchor ?? (_viewport.IsMouseOver ? ToWorld(Mouse.GetPosition(_viewport)) : VisibleCenter());
            if (anchor is null) target += new Vector(repeat * 24, repeat * 24);
            if (PasteSelection(payload, target)) { _lastPastedPayload = payload; _pasteRepeat = repeat + 1; Focus(); }
        }
        catch (Exception error) when (error is ExternalException or System.Security.SecurityException)
        { SetMessage("The clipboard is busy. Try Paste again.", isError: true); }
    }
    private bool ClipboardHasNodes()
    {
        if (!_useSystemClipboard) return _practiceClipboard is not null;
        try { return Clipboard.ContainsData(GraphClipboard.FormatName); }
        catch (Exception error) when (error is ExternalException or System.Security.SecurityException) { return false; }
    }
    private void AddSelectionMenuItems(ContextMenu menu, Point position)
    {
        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C", IsEnabled = _selectedNodes.Count > 0 };
        copy.Click += (_, _) => CopyToClipboard(); menu.Items.Add(copy);
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = _mixer.CanEditLayout && ClipboardHasNodes() };
        paste.Click += (_, _) => PasteFromClipboard(position); menu.Items.Add(paste);
        var remove = new MenuItem { Header = _selectedNodes.Count > 1 ? $"Delete {_selectedNodes.Count} nodes" : "Delete node", InputGestureText = "Delete", IsEnabled = _mixer.CanEditLayout && _selectedNodes.Count > 0 };
        remove.Click += (_, _) => DeleteSelection(); menu.Items.Add(remove);
        if (CreateNodeAlignmentMenuItem() is { } align) menu.Items.Add(align);
    }
    internal MenuItem? CreateNodeAlignmentMenuItem()
    {
        if (Graph.Nodes.Count(node => _selectedNodes.Contains(node.Id)) < 2) return null;
        var menu = new MenuItem { Header = "Align selected nodes", IsEnabled = CanAlignSelectedNodes };
        var sizes = CanAlignSelectedNodes ? NodeAlignmentSizes(_selectedNodes) : new Dictionary<string, Size>();
        void Add(string title, NodeAlignment alignment, string shortcut)
        {
            var action = new MenuItem { Header = title, InputGestureText = shortcut,
                IsEnabled = CanAlignSelectedNodes && GraphNodeAlignment.Plan(Graph, _selectedNodes, alignment, sizes).Count > 0 };
            action.Click += (_, _) => AlignSelectedNodes(alignment);
            menu.Items.Add(action);
        }
        Add("Left edges", NodeAlignment.Left, "Alt+Y");
        Add("Top edges", NodeAlignment.Top, "Alt+X");
        Add("Auto Align", NodeAlignment.Auto, "Alt+A");
        return menu;
    }
    internal bool TryNodeAlignmentShortcut(Key key, ModifierKeys modifiers, DependencyObject? focused)
    {
        if (modifiers != ModifierKeys.Alt || !ReferenceEquals(focused, this) || !CanAttemptNodeAlignment
            || Graph.Nodes.Count(node => _selectedNodes.Contains(node.Id)) < 2) return false;
        NodeAlignment? alignment = key switch { Key.X => NodeAlignment.Top, Key.Y => NodeAlignment.Left, Key.A => NodeAlignment.Auto, _ => null };
        if (alignment is null) return false;
        if (!RequireLayoutEdit()) return false;
        AlignSelectedNodes(alignment.Value);
        // Consume an eligible, already-aligned selection too, so Alt does not
        // transfer focus into menu access-key mode or add another history step.
        return true;
    }
    private bool CanAttemptNodeAlignment => _mixer.CanEdit && !_mixer.Applying && IsEnabled && CanFrameSelection;
    private bool CanAlignSelectedNodes => !_mixer.IsLayoutLocked && CanAttemptNodeAlignment;
    private Dictionary<string, Size> NodeAlignmentSizes(IEnumerable<string> ids)
    {
        // Use the current controls' real dimensions, including taller effects,
        // before arranging a row or column. No popup or native window is needed.
        _world.UpdateLayout();
        var sizes = new Dictionary<string, Size>(StringComparer.Ordinal);
        foreach (string id in ids)
            if (_nodes.TryGetValue(id, out var view)) sizes[id] = view.RenderSize;
        return sizes;
    }
    internal bool AlignSelectedNodes(NodeAlignment alignment)
    {
        if (!CanAttemptNodeAlignment || Graph.Nodes.Count(node => _selectedNodes.Contains(node.Id)) < 2 || !RequireLayoutEdit()) return false;
        string[] selected = _selectedNodes.ToArray();
        NodeAlignment resolved = alignment == NodeAlignment.Auto ? GraphNodeAlignment.ResolveAuto(Graph, selected) : alignment;
        var sizes = NodeAlignmentSizes(selected);
        if (GraphNodeAlignment.Plan(Graph, selected, resolved, sizes).Count == 0) return false;
        var layouts = Graph.Nodes.Where(node => selected.Contains(node.Id))
            .ToDictionary(node => node.Id, node => (node.Kind, node.DynamicsUiVersion));
        string description = alignment == NodeAlignment.Auto
            ? resolved == NodeAlignment.Left ? "Auto aligned nodes vertically" : "Auto aligned nodes horizontally"
            : resolved == NodeAlignment.Left ? "Aligned node left edges" : "Aligned node top edges";
        bool applied;
        string error;
        _mixer.BeginEditGesture(description);
        try
        {
            applied = _mixer.TryEdit((MixerConfiguration configuration, out string replayError) =>
            {
                // Recompute against the current graph if a queued edit is
                // replayed after an external update. The measured dimensions
                // belong to these layouts; refuse a changed layout atomically.
                if (configuration.Graph.Nodes.Any(node => layouts.TryGetValue(node.Id, out var layout)
                    && layout != (node.Kind, node.DynamicsUiVersion)))
                {
                    replayError = "A selected node changed. Try aligning again.";
                    return false;
                }
                var positions = GraphNodeAlignment.Plan(configuration.Graph, selected, resolved, sizes);
                foreach (var node in configuration.Graph.Nodes)
                    if (positions.TryGetValue(node.Id, out var position)) { node.X = position.X; node.Y = position.Y; }
                replayError = "";
                return true;
            }, out error);
        }
        finally { _mixer.EndEditGesture(); }
        if (!applied) { if (error.Length > 0) SetMessage(error, isError: true); return false; }
        SetMessage(resolved == NodeAlignment.Left ? "Selected nodes aligned in a spaced column." : "Selected nodes aligned in a spaced row.");
        return true;
    }
    internal void PopulateNodeMenu(ContextMenu menu, string id, Action rename)
    {
        Focus();
        if (!_selectedNodes.Contains(id)) SelectNode(id);
        menu.Items.Clear(); AddSelectionMenuItems(menu, ToWorld(Mouse.GetPosition(_viewport)));
        menu.Items.Add(CreateFrameSelectionMenuItem());
        var node = Graph.Nodes.FirstOrDefault(n => n.Id == id);
        if (node?.Kind is "microphone" or "virtualInput" or "appInput")
        {
            var duplicate = new MenuItem { Header = "Duplicate input", IsEnabled = _mixer.CanEditLayout && _selectedNodes.Count == 1 && Graph.Nodes.Count < 32,
                ToolTip = NodeToolTip.Create("Duplicate input", "Use the same source in another part of the graph", "Copies its controls; starts without connections") };
            duplicate.Click += (_, _) => DuplicateInput(id); menu.Items.Add(duplicate);
        }
        menu.Items.Add(new Separator());
        var detach = new MenuItem { Header = "Detach node (reconnect)", InputGestureText = "Alt+drag", IsEnabled = _mixer.CanEditLayout && _selectedNodes.Count == 1,
            ToolTip = NodeToolTip.Create("Detach node", "Reconnects the surrounding audio path", "Keeps this node and its settings") };
        detach.Click += (_, _) => DetachNode(id); menu.Items.Add(detach);
        var renameItem = new MenuItem { Header = "Rename", IsEnabled = _mixer.CanEdit && _selectedNodes.Count == 1 };
        renameItem.Click += (_, _) => rename(); menu.Items.Add(renameItem);
        var reset = new MenuItem { Header = "Reset node to defaults", IsEnabled = _mixer.CanEdit && _selectedNodes.Count == 1,
            ToolTip = node?.Kind is "output" or "virtualOutput"
                ? NodeToolTip.Create("Reset node", "Restore default controls and mute this output", "Keep its connections, position and device")
                : NodeToolTip.Create("Reset node", "Restore default sound controls", "Keep its connections, position and device/app") };
        reset.Click += (_, _) => ResetNode(id); menu.Items.Add(reset);
        if (node is not null && VirtualDeviceBindings.IsVirtual(node.Kind))
            menu.Items.Add(CreateVirtualDeviceMenu(node));
        if (node is not null && !NodeCatalog.HasLevelControl(node.Kind) && node.GainDb != 0)
        {
            var legacy = new MenuItem { Header = $"Saved level: {node.GainDb:0.#} dB", ToolTip = NodeToolTip.Create("Saved level", "Applies an additional level retained from an older version") };
            var clear = new MenuItem { Header = "Reset saved level to 0 dB", IsEnabled = _mixer.CanEdit };
            clear.Click += (_, _) => NodeChanged(id, n => n.GainDb = 0);
            legacy.Items.Add(clear); menu.Items.Add(legacy);
        }
        MenuHoverHelp.Attach(menu);
    }
    private MenuItem CreateVirtualDeviceMenu(GraphNode node)
    {
        var troubleshooting = new MenuItem { Header = "Troubleshooting", IsEnabled = _mixer.CanEdit && _selectedNodes.Count == 1 };
        var devices = new MenuItem { Header = "Reassign device",
            ToolTip = NodeToolTip.Create("Virtual device", "Changes the virtual endpoint assigned to this node", "Saved selections stay assigned while disconnected") };
        troubleshooting.Items.Add(devices);
        foreach (var endpoint in _mixer.Endpoints.Where(e => VirtualDeviceBindings.Matches(node.Kind, e))
            .OrderBy(e => e.State != "Active").ThenBy(e => e.Name))
        {
            bool selected = VirtualDeviceBindings.Same(node.Device, endpoint);
            bool used = VirtualDeviceBindings.IsUsed(Graph, node.Id, endpoint);
            var item = new MenuItem { Header = endpoint.Name + (used ? " · already used" : endpoint.State != "Active" ? " · " + endpoint.State : ""),
                IsCheckable = true, IsChecked = selected, IsEnabled = !used };
            var binding = new DeviceBinding { Id = endpoint.Id, StableId = endpoint.StableId, Name = endpoint.Name, Channel = -1 };
            item.Click += (_, _) =>
            {
                foreach (var choice in devices.Items.OfType<MenuItem>()) choice.IsChecked = ReferenceEquals(choice, item);
                _mixer.Edit(c =>
                {
                    var current = c.Graph.Nodes.FirstOrDefault(n => n.Id == node.Id);
                    if (current is not null && VirtualDeviceBindings.Matches(current.Kind, endpoint)
                        && !VirtualDeviceBindings.IsUsed(c.Graph, current.Id, endpoint)) current.Device = binding.Copy();
                });
            };
            devices.Items.Add(item);
        }
        if (devices.Items.Count == 0)
            devices.Items.Add(new MenuItem { Header = node.Kind == "virtualInput" ? "VB-CABLE not detected" : "VAC Lite not detected", IsEnabled = false });
        return troubleshooting;
    }
    internal void ResetNode(string id)
    {
        if (!_mixer.CanEdit || Graph.Nodes.All(node => node.Id != id)) return;
        CancelActiveGesture();
        NodeChanged(id, GraphDefaults.ResetNodeSettings);
        SetMessage("Node controls reset to defaults. Connections and assignments kept.");
    }
    internal bool DetachNode(string id)
    {
        if (!RequireLayoutEdit()) return false;
        if (!GraphConnectionEditing.TryPrepareDetach(Graph, id, out var plan, out string error)) { SetMessage(error, isError: true); return false; }
        CancelCable(); SetMessage("Node detached; its audio path stays connected.");
        _mixer.Edit(c => { if (!GraphConnectionEditing.TryApply(c.Graph, plan!, out string applyError)) SetMessage(applyError); });
        return true;
    }
    private void ShowCanvasMenu(Point position)
    {
        Focus();
        var menu = TrackPracticeMenu(new ContextMenu());
        var add = new MenuItem { Header = "Add node", InputGestureText = "Shift+A", IsEnabled = _mixer.CanEditLayout && Graph.Nodes.Count < 32 };
        add.Click += (_, _) => ShowAddMenu(_viewport, position); menu.Items.Add(add); menu.Items.Add(new Separator());
        AddSelectionMenuItems(menu, position);
        MenuHoverHelp.Attach(menu);
        menu.PlacementTarget = _viewport; menu.IsOpen = true;
    }
    private void ShowAddMenu(FrameworkElement anchor, Point position)
    {
        if (!RequireLayoutEdit()) return;
        var menu = CreateAddMenu(position); menu.PlacementTarget = anchor; menu.IsOpen = true;
    }
    internal ContextMenu CreateAddMenu(Point position)
    {
        var menu = TrackPracticeMenu(new ContextMenu { Width = 300, MaxHeight = 520 });
        const string searchHelp = "Search node names, categories or purpose";
        var search = new TextBox { Width = 200, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3) };
        AutomationProperties.SetName(search, "Search nodes"); AutomationProperties.SetHelpText(search, searchHelp);
        var searchBox = new Grid(); searchBox.Children.Add(search);
        var placeholder = Text("Search nodes…", 13); placeholder.Margin = new Thickness(11, 0, 0, 0); placeholder.VerticalAlignment = VerticalAlignment.Center; placeholder.IsHitTestVisible = false; placeholder.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush"); searchBox.Children.Add(placeholder);
        var searchItem = new MenuItem { StaysOpenOnClick = true, Header = searchBox, ToolTip = searchHelp }; menu.Items.Add(searchItem); menu.Items.Add(new Separator());
        AutomationProperties.SetName(searchItem, "Search nodes"); AutomationProperties.SetHelpText(searchItem, searchHelp);
        var items = new List<(string Kind, MenuItem Item)>();
        var groups = new List<MenuItem>();
        MenuItem NodeItem(string kind)
        {
            var item = new MenuItem { Header = NodeCatalog.Title(kind), Icon = new Rectangle { Width = 10, Height = 10, Fill = NodeCatalog.HeaderBrush(kind) }, IsEnabled = _mixer.CanEditLayout && Graph.Nodes.Count < 32 };
            string purpose = NodeCatalog.Description(kind);
            string unavailable = Graph.Nodes.Count >= 32 ? "The workspace has reached its limit of 32 nodes. Remove a node before adding another."
                : _mixer.IsLayoutLocked ? MixerViewModel.LayoutLockedMessage : !_mixer.CanEdit ? "Wait for the audio engine before adding a node." : "";
            item.ToolTip = NodeToolTip.Create(NodeCatalog.Title(kind), purpose, unavailable);
            ToolTipService.SetShowOnDisabled(item, true);
            AutomationProperties.SetName(item, NodeCatalog.Title(kind));
            AutomationProperties.SetHelpText(item, purpose + (unavailable.Length == 0 ? "" : " " + unavailable));
            item.Click += (_, _) => { Spawn(kind, position); menu.IsOpen = false; }; return item;
        }
        foreach (var category in NodeCatalog.Categories)
        {
            var group = new MenuItem { Header = category.Title, Icon = new Rectangle { Width = 10, Height = 10, Fill = category.HeaderBrush } };
            group.ToolTip = NodeToolTip.Create(category.Title, category.Description);
            AutomationProperties.SetName(group, category.Title); AutomationProperties.SetHelpText(group, category.Description);
            foreach (string kind in category.Kinds) group.Items.Add(NodeItem(kind));
            groups.Add(group); menu.Items.Add(group);
        }
        foreach (string kind in GraphDefaults.Kinds.Where(NodeCatalog.CanCreate)) { var item = NodeItem(kind); item.Visibility = Visibility.Collapsed; menu.Items.Add(item); items.Add((kind, item)); }
        var empty = new MenuItem { Header = "No matching nodes", IsEnabled = false, Visibility = Visibility.Collapsed }; menu.Items.Add(empty);
        const string emptyHelp = "Try a node name, category or purpose, or clear the search to browse all nodes.";
        empty.ToolTip = NodeToolTip.Create("No matching nodes", emptyHelp);
        ToolTipService.SetShowOnDisabled(empty, true); AutomationProperties.SetHelpText(empty, emptyHelp);
        search.TextChanged += (_, _) =>
        {
            string query = search.Text.Trim(); bool searching = query.Length > 0;
            placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var group in groups) group.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
            foreach (var entry in items) entry.Item.Visibility = searching && NodeCatalog.MatchesSearch(entry.Kind, query) ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = searching && items.All(e => e.Item.Visibility != Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        };
        search.PreviewKeyDown += (_, e) =>
        {
            var first = items.FirstOrDefault(i => i.Item.Visibility == Visibility.Visible && i.Item.IsEnabled);
            if (e.Key == Key.Enter && first.Item is not null) { Spawn(first.Kind, position); menu.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.Down) { if (first.Item is not null) first.Item.Focus(); else groups.FirstOrDefault()?.Focus(); e.Handled = true; }
            else if (e.Key == Key.Escape) { menu.IsOpen = false; e.Handled = true; }
        };
        menu.Opened += (_, _) => search.Focus();
        MenuHoverHelp.Attach(menu);
        return menu;
    }
    public string Spawn(string kind, Point position)
    {
        if (Graph.Nodes.Count >= 32 || !NodeCatalog.CanCreate(kind) || !RequireLayoutEdit()) return "";
        var node = GraphDefaults.NewNode(kind, Math.Round(position.X / 8) * 8, Math.Round(position.Y / 8) * 8);
        int count = Graph.Nodes.Count(n => n.Kind == kind); if (count > 0) node.Name += " " + (count + 1);
        _mixer.Edit(c => { if (c.Graph.Nodes.Count < 32 && c.Graph.Nodes.All(n => n.Id != node.Id)) c.Graph.Nodes.Add(node.Copy()); }); SelectNode(node.Id);
        SetMessage(kind == "virtualOutput" ? "Virtual output added, muted. Check its connection and level before turning its mute off." : kind == "output" ? "Output added, muted. Choose a device and level before turning its mute off." : kind == "appInput" ? "Choose a running app. Capture makes a copy; the app's original output continues." : "Node added. Choose its settings, then connect its sockets.");
        return node.Id;
    }
    private void ShowEdgeMenu(FrameworkElement anchor, string id)
    {
        var menu = CreateEdgeMenu(id); if (menu.Items.Count == 0) return;
        menu.PlacementTarget = anchor; menu.IsOpen = true;
    }
    internal ContextMenu CreateEdgeMenu(string id)
    {
        var menu = TrackPracticeMenu(new ContextMenu()); var edge = Graph.Edges.FirstOrDefault(e => e.Id == id); if (edge is null) return menu;
        var expected = edge.Copy();
        bool Current() => _mixer.CanEdit && Graph.Edges.Any(e => SameConnection(e, expected));
        MenuItem AddAction(string header, Action click, bool structural = false)
        {
            var item = new MenuItem { Header = header };
            item.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(structural ? nameof(MixerViewModel.CanEditLayout) : nameof(MixerViewModel.CanEdit)) { Source = _mixer });
            item.Click += (_, _) => { if (Current() && (!structural || RequireLayoutEdit())) click(); };
            menu.Items.Add(item); return item;
        }
        if (edge.Kind == "audio")
        {
            bool value = !edge.Muted;
            AddAction(edge.Muted ? "Turn connection on" : "Turn connection off", () => ChangeConnectionFromMenu(expected,
                (_, current) => current.Muted = value, value ? "Connection turned off." : "Connection turned on."));
        }
        if (edge.GainDb != 0)
        {
            var reset = AddAction("Reset connection level to 0 dB", () => ChangeConnectionFromMenu(expected,
                (_, current) => current.GainDb = 0, "Connection level reset to 0 dB. On/off state and routing kept."));
            reset.ToolTip = NodeToolTip.Create("Reset saved connection level", "Changes only this cable's retained level",
                "Can increase volume if this cable is attenuated", "Keeps its on/off state and routing");
        }
        AddAction("Remove connection", () => ChangeConnectionFromMenu(expected,
            (graph, current) => graph.Edges.Remove(current), "Connection removed.", allowMissing: true), structural: true);
        MenuHoverHelp.Attach(menu);
        return menu;
    }
    private static bool SameConnection(GraphEdge current, GraphEdge expected)
        => current.Id == expected.Id && current.From == expected.From && current.To == expected.To && current.Kind == expected.Kind;
    private void ChangeConnectionFromMenu(GraphEdge expected, Action<GraphConfiguration, GraphEdge> change, string message, bool allowMissing = false)
    {
        if (!_mixer.CanEdit) return;
        if (!_mixer.TryEdit((MixerConfiguration configuration, out string error) =>
        {
            var edge = configuration.Graph.Edges.FirstOrDefault(e => e.Id == expected.Id);
            // A removal already committed before a lost reply is idempotent.
            if (edge is null && allowMissing) { error = ""; return true; }
            if (edge is null || !SameConnection(edge, expected)) { error = "This connection changed. Open its menu again."; return false; }
            change(configuration.Graph, edge); error = ""; return true;
        }, out string error)) { SetMessage(error, isError: true); return; }
        SetMessage(message);
    }
    internal void UpdateWires()
    {
        WireGeometryPasses++;
        foreach (var edge in Graph.Edges)
        {
            if (!_wires.TryGetValue(edge.Id, out var paths) || !_nodes.TryGetValue(edge.From, out var from) || !_nodes.TryGetValue(edge.To, out var to)) continue;
            var first = from.FindSocket(edge.Kind == "sidechain" ? "audio" : edge.Kind, true); var second = to.FindSocket(edge.Kind, false); if (first is null || second is null) continue;
            Point start = SocketPosition(first), end = SocketPosition(second);
            if (SameAnchor(paths.From, start) && SameAnchor(paths.To, end)) continue;
            var geometry = Cable(start, end); paths.Visible.Data = geometry; paths.Outline.Data = geometry;
            UpdateHitGeometry(paths);
            paths.From = start; paths.To = end; paths.Points = CablePoints(start, end); WireGeometryBuilds++;
        }
        UpdatePreview();
    }
    internal Point SocketPosition(Socket socket)
    {
        // Canvas.Left/Top already contain a dragged node's new position before
        // WPF arranges it. Resolve only the socket's local offset through the
        // visual tree so cables do not lag by a layout pass while dragging.
        var node = _nodes[socket.NodeId];
        Point local = socket.TranslatePoint(new Point(socket.ActualWidth / 2, socket.ActualHeight / 2), node);
        double x = Canvas.GetLeft(node), y = Canvas.GetTop(node);
        if (node.UseLayoutRounding)
        {
            // Match the node's arranged physical-pixel position at fractional
            // monitor DPI while still following a drag before its next layout.
            DpiScale dpi = VisualTreeHelper.GetDpi(node);
            x = Math.Round(x * dpi.DpiScaleX) / dpi.DpiScaleX;
            y = Math.Round(y * dpi.DpiScaleY) / dpi.DpiScaleY;
        }
        return new Point(x + local.X, y + local.Y);
    }
    internal Socket? FindSnapSocket(Point viewportPoint, Socket? pending = null)
    {
        Socket? nearest = null;
        double closest = SocketSnapRadius * SocketSnapRadius;
        foreach (var node in _nodes.Values)
            foreach (string kind in new[] { "audio", "control", "sidechain" })
                foreach (bool output in new[] { false, true })
                {
                    var socket = node.FindSocket(kind, output);
                    if (socket is null || socket == pending || socket.Visibility != Visibility.Visible || pending is not null && !CompatibleSockets(pending, socket)) continue;
                    Point world = SocketPosition(socket);
                    double distance = (new Point(world.X * _zoom + _panX, world.Y * _zoom + _panY) - viewportPoint).LengthSquared;
                    if (distance > closest) continue;
                    if (pending is not null)
                    {
                        string connectionKind = pending.Output ? socket.Kind : pending.Kind;
                        if (_rewireOriginal is not null && _rewireOriginal.Kind != connectionKind) continue;
                        var candidate = new GraphEdge { From = pending.Output ? pending.NodeId : socket.NodeId, To = pending.Output ? socket.NodeId : pending.NodeId, Kind = connectionKind };
                        var validation = _rewireOriginal is null ? Graph : new GraphConfiguration { Nodes = Graph.Nodes, Edges = Graph.Edges.Where(e => e.Id != _rewireOriginal.Id).ToList() };
                        if (GraphDefaults.ConnectionError(validation, candidate).Length > 0) continue;
                    }
                    closest = distance; nearest = socket;
                }
        return nearest;
    }
    private void SetSnapSocket(Socket? socket)
    {
        if (socket is not null && !IsCurrentSocket(socket)) socket = null;
        if (!ReferenceEquals(_snapSocket, socket)) _snapSocket?.SetConnectionHighlight(false);
        _snapSocket = socket;
        // Highlight the existing port instead of drawing an extra hover ring.
        // This never changes socket geometry or the generous screen-space snap.
        _snapSocket?.SetConnectionHighlight(true);
    }
    internal string? FindInsertionCandidate(string nodeId)
    {
        var node = Graph.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node is null || !GraphConnectionEditing.IsInsertable(node) || Graph.Edges.Any(e => e.From == nodeId || e.To == nodeId)
            || !_nodes.TryGetValue(nodeId, out var view)) return null;
        var bounds = new Rect(Canvas.GetLeft(view), Canvas.GetTop(view), view.ActualWidth, view.ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        bounds.Inflate(8 / _zoom, 8 / _zoom);
        string? nearest = null; double bestDistance = double.PositiveInfinity;
        foreach (var edge in Graph.Edges.Where(e => e.Kind == "audio"))
        {
            if (!_wires.TryGetValue(edge.Id, out var wire) || wire.From is not Point from || wire.To is not Point to) continue;
            double reach = Math.Max(70, Math.Abs(to.X - from.X) * .48);
            var first = new Point(from.X + reach, from.Y); var second = new Point(to.X - reach, to.Y);
            int steps = Math.Clamp((int)Math.Ceiling(((first - from).Length + (second - first).Length + (to - second).Length) * _zoom / 12), 16, 256);
            Point previous = from;
            double distance = double.PositiveInfinity;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps, u = 1 - t;
                var point = new Point(u * u * u * from.X + 3 * u * u * t * first.X + 3 * u * t * t * second.X + t * t * t * to.X,
                    u * u * u * from.Y + 3 * u * u * t * first.Y + 3 * u * t * t * second.Y + t * t * t * to.Y);
                if (SegmentIntersects(bounds, previous, point))
                {
                    var vector = point - previous;
                    double fraction = vector.LengthSquared > 0 ? Math.Clamp(Vector.Multiply(center - previous, vector) / vector.LengthSquared, 0, 1) : 0;
                    distance = Math.Min(distance, (center - (previous + vector * fraction)).LengthSquared);
                }
                previous = point;
            }
            if (distance < bestDistance && GraphConnectionEditing.InsertionError(Graph, nodeId, edge.Id).Length == 0) { nearest = edge.Id; bestDistance = distance; }
        }
        return nearest;
    }
    private static bool SegmentIntersects(Rect bounds, Point from, Point to)
    {
        double low = 0, high = 1;
        bool Clip(double origin, double delta, double min, double max)
        {
            if (Math.Abs(delta) < .0000001) return origin >= min && origin <= max;
            double start = (min - origin) / delta, end = (max - origin) / delta;
            if (start > end) (start, end) = (end, start);
            low = Math.Max(low, start); high = Math.Min(high, end); return low <= high;
        }
        return Clip(from.X, to.X - from.X, bounds.Left, bounds.Right) && Clip(from.Y, to.Y - from.Y, bounds.Top, bounds.Bottom);
    }
    private void SetInsertionEdge(string? id)
    {
        if (_insertionEdge == id) return;
        var old = _insertionEdge; _insertionEdge = id;
        foreach (string changed in new[] { old, id }.OfType<string>())
        {
            var edge = Graph.Edges.FirstOrDefault(e => e.Id == changed);
            if (edge is null || !_wires.TryGetValue(changed, out var wire)) continue;
            wire.BrushKey = changed == _selectedEdge || changed == _insertionEdge ? "NodeSelectedBrush" : edge.Muted ? "ConsoleMutedBrush" : edge.Kind == "control" ? "NodeControlBrush" : edge.Kind == "sidechain" ? "NodeSidechainBrush" : "NodeAudioBrush";
            wire.Visible.SetResourceReference(Shape.StrokeProperty, wire.BrushKey);
            wire.Visible.Opacity = changed == _insertionEdge ? 1 : edge.Muted ? .4 : edge.Kind == "control" ? .75 : .95;
            SetWireWidth(changed, wire);
        }
        if (id is not null) SetMessage("Release to insert this node into the highlighted cable. Escape cancels.");
        else if (old is not null) SetMessage("");
    }
    private void ClearPreview() { if (_previewWire.Data is not null) _previewWire.Data = null; _previewFrom = _previewTo = null; }
    private void UpdatePreview()
    {
        if (_rewireOriginal is { } original && !Graph.Edges.Any(e => e.Id == original.Id && e.From == original.From && e.To == original.To && e.Kind == original.Kind && e.GainDb == original.GainDb && e.Muted == original.Muted))
        { CancelCable(); return; }
        if (_inputLinkSocket is not null) { ClearPreview(); return; }
        if (_snapSocket is not null && !IsCurrentSocket(_snapSocket)) SetSnapSocket(null);
        if (_pendingSocket is null || !IsCurrentSocket(_pendingSocket))
        {
            if (_pendingSocket is not null) CancelCable();
            ClearPreview(); return;
        }
        string previewKind = _rewireOriginal?.Kind ?? (_pendingSocket.Output ? _snapSocket?.Kind ?? _pendingSocket.Kind : _pendingSocket.Kind);
        string brushKey = previewKind == "control" ? "NodeControlBrush" : previewKind == "sidechain" ? "NodeSidechainBrush" : "NodeAudioBrush";
        if (_previewBrushKey != brushKey) { _previewWire.SetResourceReference(Shape.StrokeProperty, brushKey); _previewBrushKey = brushKey; }
        Point socket = SocketPosition(_pendingSocket);
        Point target = _snapSocket is not null ? SocketPosition(_snapSocket) : _pointer;
        Point start = _pendingSocket.Output ? socket : target, end = _pendingSocket.Output ? target : socket;
        if (SameAnchor(_previewFrom, start) && SameAnchor(_previewTo, end)) return;
        _previewWire.Data = Cable(start, end); _previewFrom = start; _previewTo = end;
    }
    private bool IsCurrentSocket(Socket socket) => _nodes.TryGetValue(socket.NodeId, out var node)
        && ReferenceEquals(node.FindSocket(socket.Kind, socket.Output), socket);
    private static Geometry Cable(Point from, Point to)
    {
        double reach = Math.Max(70, Math.Abs(to.X - from.X) * .48);
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            drawing.BeginFigure(from, false, false);
            drawing.BezierTo(new Point(from.X + reach, from.Y), new Point(to.X - reach, to.Y), to, true, false);
        }
        geometry.Freeze(); return geometry;
    }
    private static bool SameAnchor(Point? cached, Point current)
    {
        // WPF's coordinate transforms can round-trip through the canvas scale,
        // leaving tiny floating-point differences after zooming. One millionth
        // of a world DIP is far below a visible movement even at maximum zoom.
        const double tolerance = .000001;
        return cached is Point previous && Math.Abs(previous.X - current.X) <= tolerance && Math.Abs(previous.Y - current.Y) <= tolerance;
    }
    private void SetWireWidth(string id, WireVisual wire)
    {
        double width = id == _selectedEdge || id == _insertionEdge || id == _inputLinkCandidate || _cutEdges.ContainsKey(id) ? 3 / _zoom : Math.Max(2.5, 1.6 / _zoom);
        if (wire.Visible.StrokeThickness != width) wire.Visible.StrokeThickness = width;
        if (wire.Outline.StrokeThickness != width + 2 / _zoom) wire.Outline.StrokeThickness = width + 2 / _zoom;
        if (wire.Hit.StrokeThickness != 16 / _zoom)
        {
            wire.Hit.StrokeThickness = 16 / _zoom;
            UpdateHitGeometry(wire);
        }
    }
    private static void UpdateHitGeometry(WireVisual wire)
    {
        if (wire.Visible.Data is not Geometry worldGeometry) return;
        // WPF keeps a tooltip open inside its owner's RenderSize. World-space
        // paths can draw outside that rectangle at negative graph coordinates.
        // Give only the invisible hit path local, padded bounds; translate it
        // back exactly so the visible curve and cable editing remain in world space.
        Rect bounds = worldGeometry.Bounds;
        double padding = wire.Hit.StrokeThickness / 2 + 1;
        var origin = new Point(bounds.Left - padding, bounds.Top - padding);
        var localGeometry = worldGeometry.Clone();
        localGeometry.Transform = new TranslateTransform(-origin.X, -origin.Y);
        localGeometry.Freeze();
        var placement = new TranslateTransform(origin.X, origin.Y); placement.Freeze();
        wire.Hit.Data = localGeometry;
        wire.Hit.Width = bounds.Width + padding * 2;
        wire.Hit.Height = bounds.Height + padding * 2;
        wire.Hit.RenderTransform = placement;
    }
    private static DoubleCollection CreateMutedWireDashes() { var dashes = new DoubleCollection([2, 3]); dashes.Freeze(); return dashes; }
    private readonly record struct ConnectionDetailKey(string From, string To, string SourceName, string TargetName, string Kind, double GainDb, bool Muted);
    private sealed class WireVisual(System.Windows.Shapes.Path visible, System.Windows.Shapes.Path hit, System.Windows.Shapes.Path outline)
    {
        public System.Windows.Shapes.Path Visible { get; } = visible;
        public System.Windows.Shapes.Path Hit { get; } = hit;
        public System.Windows.Shapes.Path Outline { get; } = outline;
        public Point? From, To;
        public IReadOnlyList<Point> Points = [];
        public string SourceId = "", TargetId = "", Kind = "", BrushKey = "";
        public ConnectionDetailKey? DetailKey;
    }
    private static T? FindParent<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null) { if (current is T result) return result; current = current is Visual || current is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current); }
        return null;
    }
    private static IReadOnlyList<Point> CablePoints(Point from, Point to)
    {
        double reach = Math.Max(70, Math.Abs(to.X - from.X) * .48);
        var a = new Point(from.X + reach, from.Y); var b = new Point(to.X - reach, to.Y);
        int steps = Math.Clamp((int)Math.Ceiling(((a - from).Length + (b - a).Length + (to - b).Length) / 6), 24, 512);
        var points = new Point[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            double t = (double)i / steps, u = 1 - t;
            points[i] = new(u*u*u*from.X + 3*u*u*t*a.X + 3*u*t*t*b.X + t*t*t*to.X, u*u*u*from.Y + 3*u*u*t*a.Y + 3*u*t*t*b.Y + t*t*t*to.Y);
        }
        return points;
    }
    internal sealed class Socket : Button
    {
        private readonly Shape _shape;
        internal bool IsConnectionHighlighted { get; private set; }
        internal bool IsConnected { get; private set; }
        public string NodeId { get; }
        public string Kind { get; }
        public bool Output { get; }
        internal void SetCollapsed(bool collapsed)
        {
            // Keep adjacent input hit regions disjoint, including after WPF
            // rounds their positions. Invisible targets must not cover peers.
            Width = collapsed ? 12 : 24;
            Height = collapsed ? 12 : 20;
        }
        public Socket(GraphWorkspace owner, string id, string kind, bool output)
        {
            NodeId = id; Kind = kind; Output = output; Width = 24; Height = 20; MinHeight = 0; Padding = new Thickness(0); Background = Brushes.Transparent; BorderThickness = new Thickness(0);
            // The border's centre can fall between layout pixels. Keep the
            // glyph and cable anchored to that same point at every zoom.
            UseLayoutRounding = false; SnapsToDevicePixels = false;
            var socketTemplate = new ControlTemplate(typeof(Button)); var target = new FrameworkElementFactory(typeof(Border)); target.SetValue(Border.BackgroundProperty, Brushes.Transparent); var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); target.AppendChild(content); socketTemplate.VisualTree = target; Template = socketTemplate;
            var shape = _shape = kind == "control" ? (Shape)new Rectangle { Width = 10, Height = 10, RadiusX = 1.5, RadiusY = 1.5 }
                : kind == "sidechain" ? new Polygon { Points = new PointCollection([new(0, 5), new(5, 0), new(10, 5), new(5, 10)]), Width = 12, Height = 12, Stretch = Stretch.Fill }
                : new Ellipse { Width = 12, Height = 12 };
            shape.UseLayoutRounding = false; shape.SnapsToDevicePixels = false; shape.StrokeThickness = 1.6;
            UpdateHighlight();
            MouseEnter += (_, _) => UpdateHighlight();
            MouseLeave += (_, _) => UpdateHighlight();
            GotKeyboardFocus += (_, _) => UpdateHighlight();
            LostKeyboardFocus += (_, _) => UpdateHighlight();
            Content = shape; Cursor = Cursors.Cross; ToolTip = kind == "control"
                ? NodeToolTip.Create(output ? "Linked level output" : "Linked level input", output
                    ? "Shares this controller's level adjustment with connected nodes"
                    : "Applies a connected controller's level adjustment to this node")
                : kind == "sidechain" ? NodeToolTip.Create("Trigger / Sidechain detector", "Uses one audio source to control Static or Dynamic ducking", "Trigger audio stays out of the audible output")
                : output ? NodeToolTip.Create("Audio output", "Carries this node's audio to connected audio or trigger inputs")
                    : NodeToolTip.Create("Audio input", "Combines audio from connected outputs");
            AutomationProperties.SetName(this, (output ? "Output" : "Input") + " " + kind + " socket");
            AutomationProperties.SetItemStatus(this, "Not connected");
            PreviewMouseLeftButtonDown += (_, e) => owner.SocketPressed(this, e);
            Click += (_, _) => { if (!owner.RequireLayoutEdit()) return; if (owner._pendingSocket is null) { owner._pendingSocket = this; owner.SetMessage("Choose a matching socket to complete the connection."); } else if (owner._pendingSocket != this) owner.Connect(owner._pendingSocket, this); };
        }
        internal void SetConnectionHighlight(bool highlighted)
        {
            IsConnectionHighlighted = highlighted;
            UpdateHighlight();
        }
        internal void SetConnected(bool connected)
        {
            if (IsConnected == connected) return;
            IsConnected = connected;
            AutomationProperties.SetItemStatus(this, connected ? "Connected" : "Not connected");
            UpdateHighlight();
        }
        private void UpdateHighlight()
        {
            string colour = Kind == "control" ? "NodeControlBrush" : Kind == "sidechain" ? "NodeSidechainBrush" : "NodeAudioBrush";
            _shape.SetResourceReference(Shape.FillProperty, IsConnected ? colour : "NodeCanvasBrush");
            _shape.SetResourceReference(Shape.StrokeProperty,
                IsKeyboardFocused || IsConnectionHighlighted || IsMouseOver ? "NodeSelectedBrush" : IsConnected ? "NodeCanvasBrush" : colour);
        }
    }
    private sealed class CanvasGrid : FrameworkElement
    {
        private double _panX, _panY, _zoom = 1, _tileSpacing;
        private Brush? _tileInk;
        private readonly DrawingBrush _tile = new() { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, Opacity = .8 };
        public long TileRebuilds { get; private set; }
        public long RenderCount { get; private set; }
        public double Spacing { get; private set; } = 20;
        // Include one partly visible tile on each axis in this conservative bound.
        public int DotCountBound => (int)((Math.Ceiling(ActualWidth / Spacing) + 1) * (Math.Ceiling(ActualHeight / Spacing) + 1));
        public CanvasGrid() { IsHitTestVisible = false; }
        public void SetView(double panX, double panY, double zoom)
        {
            if (_panX == panX && _panY == panY && _zoom == zoom) return;
            _panX = panX; _panY = panY; _zoom = zoom;
            // Coarsen by whole world-grid subdivisions rather than drawing
            // ever more dots as the user zooms out. The grid stays world-anchored.
            double spacing = 20 * zoom;
            while (spacing < 16) spacing *= 2;
            Spacing = spacing;
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            RenderCount++;
            dc.DrawRectangle(ConsoleTheme.Get("NodeCanvasBrush"), null, new Rect(RenderSize));
            var ink = ConsoleTheme.Get("NodeGridBrush");
            if (_tileSpacing != Spacing || !ReferenceEquals(_tileInk, ink))
            {
                var drawing = new DrawingGroup();
                // The transparent extent defines one complete tile; the dot
                // remains one DIP wide instead of stretching to fill it.
                drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Spacing, Spacing))));
                drawing.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
                drawing.Freeze();
                _tile.Drawing = drawing; _tile.Viewbox = new Rect(0, 0, Spacing, Spacing);
                _tileSpacing = Spacing; _tileInk = ink; TileRebuilds++;
            }
            var viewport = new Rect((_panX % Spacing + Spacing) % Spacing, (_panY % Spacing + Spacing) % Spacing, Spacing, Spacing);
            if (_tile.Viewport != viewport) _tile.Viewport = viewport;
            // One retained tiled draw replaces thousands of per-dot commands.
            dc.DrawRectangle(_tile, null, new Rect(RenderSize));
        }
    }
}
