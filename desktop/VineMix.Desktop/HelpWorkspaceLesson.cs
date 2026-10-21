using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>The real editor, backed by its own silent model and private clipboard.</summary>
internal sealed class HelpWorkspaceLesson : IHelpLesson
{
    private readonly StackPanel _view = new();
    private readonly Grid _canvasHost = new() { Height = 360, ClipToBounds = true };
    private readonly Border _entryGate;
    private readonly Button _enter, _exit;
    private readonly TextBlock _mode;
    private bool _active, _disposed, _needsFit = true;

    public FrameworkElement View => _view;
    internal MixerViewModel Mixer { get; }
    internal GraphWorkspace Workspace { get; }
    internal bool IsPracticeActive => _active;

    internal HelpWorkspaceLesson(bool navigation = false)
    {
        Mixer = new MixerViewModel(preview: true);
        Mixer.ApplyPreviewSnapshot(CreateSnapshot());
        // Every binding below this boundary resolves against the practice model.
        _view.DataContext = Mixer;
        Workspace = new GraphWorkspace(Mixer, useSystemClipboard: false)
        {
            DataContext = Mixer,
            IsEnabled = false,
            IsHitTestVisible = false,
            Focusable = false
        };
        Workspace.SetResourceReference(Control.BackgroundProperty, "NodeCanvasBrush");
        AutomationProperties.SetName(Workspace, "Silent practice canvas");
        AutomationProperties.SetHelpText(Workspace,
            "An isolated practice graph. Enter canvas to edit nodes, connect sockets and zoom. " +
            "Node copy and paste stay inside this example. Exit canvas returns wheel scrolling to Help.");

        var entryBar = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        _enter = HelpLessonUi.Button("Enter canvas", EnterPractice,
            "Give the practice canvas its own keyboard and zoom controls.");
        _exit = HelpLessonUi.Button("Exit canvas", ExitPractice,
            "Stop practicing and return the mouse wheel to scrolling help.");
        _enter.Margin = _exit.Margin = new Thickness(0, 0, 12, 0);
        _exit.Visibility = Visibility.Collapsed;
        actions.Children.Add(_enter); actions.Children.Add(_exit);
        DockPanel.SetDock(actions, Dock.Left); entryBar.Children.Add(actions);
        _mode = HelpLessonUi.Hint("Sample workspace. No audio.");
        _mode.Margin = new Thickness(0);
        _mode.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLiveSetting(_mode, AutomationLiveSetting.Polite);
        entryBar.Children.Add(_mode);
        _view.Children.Add(entryBar);

        _canvasHost.Children.Add(Workspace);
        _entryGate = new Border { Background = Brushes.Transparent, Focusable = false };
        _canvasHost.Children.Add(_entryGate);
        var canvasBorder = new Border
        {
            Child = _canvasHost, BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10)
        };
        canvasBorder.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
        canvasBorder.SetResourceReference(Border.BackgroundProperty, "NodeCanvasBrush");
        _view.Children.Add(canvasBorder);

        UIElement[] instructions = navigation
            ? [HelpLessonUi.Hint("Scroll to zoom. Middle-drag or Space+drag pans."),
                HelpLessonUi.Hint("F frames selected nodes. Home or Fit shows all; Ctrl+0 restores 100%."),
                HelpLessonUi.Hint("Exit canvas returns the wheel to scrolling Help.")]
            : [HelpLessonUi.Hint("Drag headers to move nodes. Shift-click selects more; double-click a title to rename."),
                HelpLessonUi.Hint("Drag between matching sockets. Right-click a cable for its actions."),
                HelpLessonUi.Hint("Round Audio sockets carry sound; diamond Trigger sockets detect it; square Control sockets share level."),
                HelpLessonUi.Hint("Add creates a node. Ctrl+C / Ctrl+V copies nodes within this sample. Ctrl+Z undoes.")];
        _view.Children.Add(HelpLessonUi.Section("", "", instructions));

        // Parent help owns scrolling until the user explicitly enters this example.
        _canvasHost.PreviewMouseWheel += CanvasMouseWheel;
        _view.Loaded += ViewLoaded;
        _view.Unloaded += (_, _) => Suspend();
        _canvasHost.SizeChanged += (_, _) => FitWhenReady();
    }

    private void ViewLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed) return;
        _view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitWhenReady));
    }

    private void FitWhenReady()
    {
        if (!_needsFit || _disposed || !Workspace.IsLoaded || _canvasHost.ActualWidth <= 0) return;
        _needsFit = false;
        Workspace.UpdateLayout();
        Workspace.FitAll();
    }

    internal void EnterPractice()
    {
        if (_disposed) return;
        _active = true;
        Workspace.IsEnabled = true;
        Workspace.IsHitTestVisible = Workspace.Focusable = true;
        _entryGate.Visibility = Visibility.Collapsed;
        _enter.Visibility = Visibility.Collapsed; _exit.Visibility = Visibility.Visible;
        _mode.Text = "Scroll to zoom. No audio.";
        FitWhenReady();
        Workspace.Focus();
    }

    private void ExitPractice()
    {
        Suspend();
        _enter.Focus();
    }

    private void CanvasMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (_active && Workspace.IsKeyboardFocusWithin) return;
        // Intercept before GraphWorkspace's viewport receives the preview route.
        // This preserves ordinary help scrolling over the inactive example.
        if (Ancestor<ScrollViewer>(_view) is { } scroll)
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - args.Delta / 3.0);
        args.Handled = true;
    }

    public void Reset()
    {
        if (_disposed) return;
        Suspend();
        Mixer.ApplyPreviewSnapshot(CreateSnapshot());
        Workspace.ResetPracticeState();
        _needsFit = true;
        FitWhenReady();
    }

    public bool TryCancel()
    {
        if (_disposed) return false;
        bool cancelled = Workspace.ClosePracticeMenus();
        foreach (var element in Descendants(Workspace).ToArray())
        {
            if (element is GraphNodeView node) cancelled |= node.CancelPracticeRename();
            if (element is FrameworkElement { ContextMenu: { IsOpen: true } menu })
            { menu.IsOpen = false; cancelled = true; }
            if (element is ComboBox { IsDropDownOpen: true } choice)
            { choice.IsDropDownOpen = false; cancelled = true; }
            if (element is RotaryValueControl dial) cancelled |= dial.TryCancelPracticeInteraction();
            if (element is EqualizerEditor eq) cancelled |= eq.TryCancelPracticeInteraction();
            if (element is VoiceColorPad { IsAdjusting: true } pad)
            { pad.EndAdjustment(); cancelled = true; }
        }
        if (Workspace.HasActiveGesture)
        { Workspace.CancelActiveGesture(); cancelled = true; }
        return cancelled;
    }

    public void Suspend()
    {
        if (_disposed) return;
        Workspace.ClosePracticeMenus();
        foreach (var element in Descendants(Workspace).ToArray())
        {
            if (element is GraphNodeView node) node.CancelPracticeRename();
            if (element is FrameworkElement { ContextMenu: { } menu }) menu.IsOpen = false;
            if (element is ComboBox choice) choice.IsDropDownOpen = false;
            if (element is RotaryValueControl dial) dial.SuspendPracticeInteraction();
            if (element is EqualizerEditor eq) eq.SuspendPracticeInteraction();
            if (element is VoiceColorPad pad) pad.EndAdjustment();
        }
        Workspace.CancelActiveGesture();
        Mixer.EndEditGesture();
        if (Mouse.Captured is DependencyObject captured && IsWithin(captured, Workspace))
            Mouse.Captured.ReleaseMouseCapture();
        _active = false;
        Workspace.IsEnabled = false;
        Workspace.IsHitTestVisible = Workspace.Focusable = false;
        _entryGate.Visibility = Visibility.Visible;
        _enter.Visibility = Visibility.Visible; _exit.Visibility = Visibility.Collapsed;
        _mode.Text = "Sample workspace. No audio.";
        if (_view.IsLoaded && _enter.IsVisible && Workspace.IsKeyboardFocusWithin) _enter.Focus();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Suspend();
        // Removing the control unloads node controls and their theme subscriptions.
        _canvasHost.Children.Remove(Workspace);
        Mixer.Dispose();
        _disposed = true;
    }

    private static MixerSnapshot CreateSnapshot()
    {
        var snapshot = new MixerSnapshot { Configuration = new MixerConfiguration { Version = 2 } };
        var microphone = GraphDefaults.NewNode("microphone", 0, 0);
        microphone.Id = "practice-input"; microphone.Name = "Sample microphone";
        microphone.Device = new() { Id = "preview-practice-input", Name = "Sample microphone", Channel = -1 };
        var ducking = GraphDefaults.NewNode("ducking", 416, 0);
        ducking.Id = "practice-ducking"; ducking.Name = "Try these sockets";
        var output = GraphDefaults.NewNode("output", 832, 0);
        output.Id = "practice-output"; output.Name = "Sample headphones";
        output.Device = new() { Id = "preview-practice-output", Name = "Sample headphones", Channel = -1 };
        var control = GraphDefaults.NewNode("control", 0, 302);
        control.Id = "practice-control"; control.Name = "Linked control";
        snapshot.Configuration.Graph = new()
        {
            Nodes = [microphone, ducking, output, control],
            Edges =
            [
                new() { Id = "practice-audio-in", From = microphone.Id, To = ducking.Id },
                new() { Id = "practice-audio-out", From = ducking.Id, To = output.Id },
                new() { Id = "practice-trigger", From = microphone.Id, To = ducking.Id, Kind = "sidechain" },
                new() { Id = "practice-level", From = control.Id, To = ducking.Id, Kind = "control" }
            ]
        };
        snapshot.Endpoints =
        [
            new() { Id = microphone.Device.Id, Name = microphone.Device.Name, Direction = "capture", State = "Active", Channels = 2, Rate = 48000 },
            new() { Id = "preview-practice-input-two", Name = "Another sample microphone", Direction = "capture", State = "Active", Channels = 1, Rate = 48000 },
            new() { Id = output.Device.Id, Name = output.Device.Name, Direction = "render", State = "Active", Channels = 2, Rate = 48000 }
        ];
        snapshot.Applications = [new() { ProcessId = 101, Name = "Sample music app", ExecutablePath = "preview-practice-music.exe" }];
        snapshot.Status = new()
        {
            AudioFrames = 48000,
            Nodes = snapshot.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            {
                Id = node.Id, Channels = 2, Peak = node.Kind == "microphone" ? .15f : .07f,
                PeakLeft = node.Kind == "microphone" ? .15f : .07f, PeakRight = .04f,
                GainReductionDb = node.Kind == "ducking" ? 6 : 0, DetectorDb = -18, DynamicsFrame = 48000,
                Path = new() { State = "Running", EndpointId = node.Device.Id, Channels = 2, Rate = 48000 }
            }).ToList()
        };
        return snapshot;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        if (parent is not Visual) yield break;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }

    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        for (var current = Parent(element); current is not null; current = Parent(current))
            if (current is T match) return match;
        return null;
    }

    private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
    {
        for (DependencyObject? current = element; current is not null; current = Parent(current))
            if (ReferenceEquals(current, ancestor)) return true;
        return false;
    }

    private static DependencyObject? Parent(DependencyObject? element)
        => element is Visual ? VisualTreeHelper.GetParent(element)
            : element is FrameworkContentElement content ? content.Parent : null;
}
