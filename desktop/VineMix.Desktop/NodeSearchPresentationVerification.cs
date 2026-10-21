using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Detached search/navigation checks; no native focus, windows, input injection or engine access.</summary>
internal static class NodeSearchPresentationVerification
{
    internal static async Task RunAsync(MainWindow window, MixerViewModel mixer, FrameworkElement root,
        Func<Task> layout, Action<bool, string> require, Action<string>? render = null)
    {
        require(mixer.IsPreview && PresentationSource.FromVisual(root) is null,
            "Node-search presentation checks use only a detached preview workspace.");
        var workspace = window.Workspace;
        var search = window.SearchView;
        var fixture = UiPreview.CreateSnapshot([]);
        var first = GraphDefaults.NewNode("microphone", -600, -300);
        first.Id = "find-voice-first"; first.Name = "Broadcast Voice";
        first.Device = new() { Id = "preview-mic1", Name = "Studio USB microphone" };
        var second = first.Copy(); second.Id = "find-voice-second"; second.X = 4200; second.Y = 2400;
        var app = GraphDefaults.NewNode("appInput", 8000, -2200);
        app.Id = "find-music"; app.Name = "Music";
        app.App = new() { ProcessId = 100, ExecutablePath = "preview-spotify.exe", Name = "Spotify" };
        var eq = GraphDefaults.NewNode("eq", 0, 0); eq.Id = "find-eq"; eq.Name = "Vocal tone";
        fixture.Configuration.Graph = new()
        {
            Nodes = [first, second, app, eq],
            Edges = [new() { Id = "find-route", From = first.Id, To = eq.Id, Kind = "audio" }],
            PanX = -140, PanY = 75, Zoom = .5
        };
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        // A nonempty existing history catches accidental clearing as well as newly added search edits.
        workspace.NodeChanged(first.Id, node => node.GainDb = -3); await layout();
        workspace.SelectNode(first.Id); workspace.SelectNodeForPointer(eq.Id, ModifierKeys.Shift);
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(view => view.Id == id);
        var world = (Canvas)View(first.Id).Parent;
        var viewport = (FrameworkElement)VisualTreeHelper.GetParent(world);
        Matrix Camera() => ((MatrixTransform)world.RenderTransform).Matrix;
        string Audio() => JsonSerializer.Serialize(new { mixer.GraphConfig.Nodes, mixer.GraphConfig.Edges }, Protocol.Json);
        string[] ResultIds() => search.Results.Items.Cast<NodeSearchResult>().Select(result => result.Id).ToArray();
        string? SelectedId() => (search.Results.SelectedItem as NodeSearchResult)?.Id;
        bool Selected(params string[] ids) => workspace.SelectedNodeIds.ToHashSet().SetEquals(ids);
        void RequireCentered(string id)
        {
            var view = View(id);
            var bounds = view.TransformToAncestor(viewport).TransformBounds(new Rect(view.RenderSize));
            require(Math.Abs(bounds.Left + bounds.Width / 2 - viewport.ActualWidth / 2) <= 1
                && Math.Abs(bounds.Top + bounds.Height / 2 - viewport.ActualHeight / 2) <= 1
                && Camera().M11 >= .3 && Camera().M11 <= 1,
                "Finding " + id + " centers its actual rendered bounds at a readable bounded zoom.");
        }
        string audio = Audio(), saveStatus = mixer.SaveStatus;
        long presentations = mixer.ConfigurationPresentationCount;
        int history = mixer.EditHistory.Entries.Count, pending = mixer.PendingEditCount;
        string undo = mixer.UndoDescription;
        void RequireNoEdit(string description) => require(Audio() == audio
            && mixer.ConfigurationPresentationCount == presentations && mixer.EditHistory.Entries.Count == history
            && mixer.PendingEditCount == pending && mixer.UndoDescription == undo && mixer.SaveStatus == saveStatus, description);
        Matrix initialCamera = Camera();
        var initialFocus = Keyboard.FocusedElement;
        double oldWidth = root.Width, oldHeight = root.Height;
        try
        {
            require(history > 0 && !window.IsNodeSearchOpen && workspace.IsEnabled,
                "Search starts hidden and its fixture includes an existing undoable audio edit.");
            foreach (ModifierKeys modifiers in new[] { ModifierKeys.None, ModifierKeys.Shift, ModifierKeys.Alt,
                ModifierKeys.Control | ModifierKeys.Shift, ModifierKeys.Control | ModifierKeys.Alt, ModifierKeys.Control | ModifierKeys.Windows })
                require(!window.TryNodeSearchShortcut(Key.F, modifiers, workspace) && !window.IsNodeSearchOpen,
                    "Only plain Ctrl+F opens node search; " + modifiers + " leaves the canvas alone.");
            var draft = new TextBox { Text = "-12.75" };
            foreach (DependencyObject editor in new DependencyObject[] { draft, new RichTextBox(), new PasswordBox() })
                require(!window.TryNodeSearchShortcut(Key.F, ModifierKeys.Control, editor) && !window.IsNodeSearchOpen,
                    "Ctrl+F does not move focus away from a " + editor.GetType().Name + " editor.");
            draft.Template = new ControlTemplate(typeof(TextBox)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
            draft.ApplyTemplate();
            require(!window.TryNodeSearchShortcut(Key.F, ModifierKeys.Control, VisualTreeHelper.GetChild(draft, 0))
                && draft.Text == "-12.75" && Camera() == initialCamera && Selected(first.Id, eq.Id),
                "An editor descendant also keeps its draft, selection and view when Ctrl+F is pressed.");

            var menu = MainWindow.CreateSettingsMenu(mixer, workspace.SetMessage, window.OpenHelp, window.OpenNodeSearch);
            var find = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Find node…"));
            require(find.IsEnabled && find.InputGestureText == "Ctrl+F", "Settings exposes Find node and its Ctrl+F shortcut.");
            find.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await layout();
            require(window.IsNodeSearchOpen && !workspace.IsEnabled && !menu.IsOpen
                && ResultIds().SequenceEqual([first.Id, second.Id, app.Id, eq.Id]),
                "The actual Settings action opens one modal search with all current nodes, without opening a popup.");
            var overlay = (DependencyObject)VisualTreeHelper.GetParent(search);
            require(KeyboardNavigation.GetTabNavigation(overlay) == KeyboardNavigationMode.Cycle
                && KeyboardNavigation.GetControlTabNavigation(overlay) == KeyboardNavigationMode.Cycle
                && search.SearchBox.IsTabStop && search.CloseButton.IsTabStop && search.GoButton.IsTabStop,
                "Search keeps keyboard navigation within the overlay and exposes reachable entry and action controls.");
            require(AutomationProperties.GetName(search.SearchBox) == "Search nodes"
                && AutomationProperties.GetName(search.Results) == "Matching nodes"
                && AutomationProperties.GetHelpText(search.SearchBox).Contains("Up and Down"),
                "Search exposes an accessible field name, result-list name and keyboard instructions.");
            foreach (var query in new (string Text, string[] Ids)[]
            {
                ("VOICE", [first.Id, second.Id]), ("equalizer", [eq.Id]),
                ("studio usb", [first.Id, second.Id]), ("spotify", [app.Id])
            })
            {
                search.SearchBox.Text = query.Text;
                require(ResultIds().SequenceEqual(query.Ids), "Typing '" + query.Text + "' filters the actual result list.");
            }
            search.SearchBox.Text = "voice";
            require(search.Results.Items.Cast<NodeSearchResult>().All(result => result.Name == "Broadcast Voice")
                && search.TrySearchKey(Key.Down, ModifierKeys.None) && SelectedId() == second.Id,
                "Down chooses the second identically named result by its distinct node ID.");
            require(search.TrySearchKey(Key.Down, ModifierKeys.None) && SelectedId() == second.Id
                && search.TrySearchKey(Key.Up, ModifierKeys.None) && SelectedId() == first.Id
                && search.TrySearchKey(Key.Up, ModifierKeys.None) && SelectedId() == first.Id,
                "Up and Down stay within the current results instead of wrapping or clearing selection.");
            search.TrySearchKey(Key.Down, ModifierKeys.None);
            foreach (Key key in new[] { Key.Up, Key.Down, Key.Enter })
                require(!search.TrySearchKey(key, ModifierKeys.Control) && SelectedId() == second.Id && window.IsNodeSearchOpen,
                    "Modified " + key + " remains available to the search editor without choosing a node.");
            require(search.SearchBox.Text == "voice" && ReferenceEquals(Keyboard.FocusedElement, initialFocus)
                && Camera() == initialCamera && Selected(first.Id, eq.Id),
                "Browsing results preserves typed text, keyboard focus, graph selection and camera until Go or Enter.");
            require(window.TryNodeSearchShortcut(Key.F, ModifierKeys.Control, search.SearchBox)
                && search.SearchBox.Text == "voice" && SelectedId() == second.Id,
                "Repeated Ctrl+F keeps the open search and its current query/result intact.");
            await layout();
            var previousItems = search.Results.ItemsSource;
            var previousContainer = search.Results.ItemContainerGenerator.ContainerFromIndex(1);
            mixer.RefreshPreviewTelemetry(); await layout();
            require(search.SearchBox.Text == "voice" && SelectedId() == second.Id
                && ReferenceEquals(search.Results.ItemsSource, previousItems)
                && ReferenceEquals(search.Results.ItemContainerGenerator.ContainerFromIndex(1), previousContainer),
                "A telemetry refresh preserves the query, chosen duplicate, result list and its rendered keyboard target.");
            var resultItem = search.Results.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
            require(resultItem is not null && AutomationProperties.GetName(resultItem) == "Broadcast Voice"
                && AutomationProperties.GetHelpText(resultItem).Contains("Studio USB microphone")
                && resultItem.IsTabStop && resultItem.Focusable,
                "Rendered results are keyboard reachable and expose their node name and assigned-source detail to accessibility clients.");
            render?.Invoke("find-node-results.png");

            search.SearchBox.Text = "no matching node";
            require(search.Results.Items.Count == 0 && !search.GoButton.IsEnabled && !search.TryChooseSelected()
                && search.TrySearchKey(Key.Enter, ModifierKeys.None) && window.IsNodeSearchOpen,
                "A no-match query disables Go and Enter remains a harmless search action.");
            require(Descendants<TextBlock>(search).Any(text => text.Text == "No matching nodes." && text.Visibility == Visibility.Visible),
                "An empty result list explains that no nodes matched.");
            render?.Invoke("find-node-no-results.png");
            require(window.TryNodeSearchShortcut(Key.Escape, ModifierKeys.None, search.SearchBox)
                && !window.IsNodeSearchOpen && workspace.IsEnabled && Selected(first.Id, eq.Id) && Camera() == initialCamera,
                "Escape cancels search and restores the workspace with the original multi-selection and camera intact.");
            require(!window.TryNodeSearchShortcut(Key.Escape, ModifierKeys.None, workspace),
                "With search hidden Escape remains available to the canvas.");
            RequireNoEdit("Opening, typing, browsing, refreshing and cancelling search preserve audio, pending work, save state and existing undo history.");

            require(window.TryNodeSearchShortcut(Key.F, ModifierKeys.Control, workspace) && search.SearchBox.Text.Length == 0,
                "Ctrl+F opens a fresh search from the canvas without retaining an old no-match query.");
            search.SearchBox.Text = "voice"; search.TrySearchKey(Key.Down, ModifierKeys.None);
            require(search.TrySearchKey(Key.Enter, ModifierKeys.None) && !window.IsNodeSearchOpen
                && workspace.IsEnabled && Selected(second.Id),
                "Enter closes search and selects precisely the second identically named node.");
            await layout(); RequireCentered(second.Id);
            window.OpenNodeSearch(); search.SearchBox.Text = "spotify";
            search.GoButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
            require(!window.IsNodeSearchOpen && Selected(app.Id), "The actual Go button navigates to the matching app node.");
            RequireCentered(app.Id);
            RequireNoEdit("Finding nodes changes only selection and view, without audio edits, configuration dispatch or undo-history changes.");

            window.OpenNodeSearch(); search.SearchBox.Text = "voice"; await layout();
            root.Width = 900; root.Height = 580; await layout();
            var surfaceBounds = search.TransformToAncestor(root).TransformBounds(new Rect(search.RenderSize));
            var buttonBounds = search.GoButton.TransformToAncestor(search).TransformBounds(new Rect(search.GoButton.RenderSize));
            require(surfaceBounds.Left >= 0 && surfaceBounds.Right <= root.ActualWidth
                && surfaceBounds.Top >= MainWindow.TitleBarHeight && surfaceBounds.Bottom <= root.ActualHeight
                && buttonBounds.Bottom <= search.ActualHeight && search.Results.ActualHeight > 80,
                "At the minimum app size the compact search, result area and Go action fit without clipping.");
            render?.Invoke("find-node-small.png");
            Matrix beforeClose = Camera();
            search.CloseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            require(!window.IsNodeSearchOpen && workspace.IsEnabled && Camera() == beforeClose && Selected(app.Id),
                "The actual Close button cancels without changing the current selection or view.");
            root.Width = oldWidth; root.Height = oldHeight; await layout();

            workspace.BeginNodeMove(app.Id, new Point(100, 100));
            require(!window.TryNodeSearchShortcut(Key.F, ModifierKeys.Control, workspace) && !window.IsNodeSearchOpen
                && workspace.IsNodeDragging, "Search does not interrupt an active node-move gesture.");
            workspace.CancelActiveGesture();
            RequireNoEdit("Closing search and declining it during a gesture preserve existing audio and history.");

            // A removed node can outlive the result snapshot briefly. Choosing it must revalidate the current graph.
            window.OpenNodeSearch(); search.SearchBox.Text = "spotify";
            Matrix beforeMissing = Camera();
            mixer.GraphConfig.Nodes.RemoveAll(node => node.Id == app.Id);
            require(!search.TryChooseSelected() && window.IsNodeSearchOpen && search.Results.Items.Count == 0
                && !search.GoButton.IsEnabled && Camera() == beforeMissing && Selected(app.Id),
                "A stale removed result is rejected and refreshed without selecting another node or moving the camera.");
            window.CloseNodeSearch();
            mixer.ApplyPreviewSnapshot(fixture); await layout();
            window.OpenNodeSearch(); search.SearchBox.Text = "voice"; search.TrySearchKey(Key.Down, ModifierKeys.None);
            await layout();
            previousItems = search.Results.ItemsSource;
            previousContainer = search.Results.ItemContainerGenerator.ContainerFromIndex(1);
            var signalOnly = UiPreview.CreateSnapshot(["--signal"]);
            signalOnly.Configuration.Graph = mixer.GraphConfig.Copy();
            mixer.ApplyPreviewSnapshot(signalOnly); await layout();
            require(search.SearchBox.Text == "voice" && SelectedId() == second.Id
                && ReferenceEquals(search.Results.ItemsSource, previousItems)
                && ReferenceEquals(search.Results.ItemContainerGenerator.ContainerFromIndex(1), previousContainer),
                "A fresh snapshot with the same searchable graph retains existing result containers instead of rebuilding them for meter changes.");
            var updated = UiPreview.CreateSnapshot([]); updated.Configuration.Graph = fixture.Configuration.Graph.Copy();
            updated.Configuration.Revision = fixture.Configuration.Revision + 1;
            updated.Configuration.Graph.Nodes.Single(node => node.Id == second.Id).Name = "Interview";
            updated.Configuration.Graph.Nodes.RemoveAll(node => node.Id == first.Id); updated.Configuration.Graph.Edges.Clear();
            mixer.ApplyPreviewSnapshot(updated); await layout();
            require(window.IsNodeSearchOpen && search.SearchBox.Text == "voice" && search.Results.Items.Count == 0,
                "A current-graph change while search is open immediately removes renamed/deleted matches without discarding the query.");
            search.SearchBox.Text = "interview";
            require(ResultIds().SequenceEqual([second.Id]), "The same open search finds a node under its updated name.");
            window.CloseNodeSearch();
            var empty = UiPreview.CreateSnapshot([]); empty.Configuration.Graph = new();
            mixer.ApplyPreviewSnapshot(empty); await layout();
            window.OpenNodeSearch(); await layout();
            require(search.Results.Items.Count == 0 && !search.GoButton.IsEnabled
                && Descendants<TextBlock>(search).Any(text => text.Text == "No nodes in this workspace." && text.Visibility == Visibility.Visible),
                "An empty graph gives a distinct helpful empty state with no enabled navigation action.");
            require(new WindowInteropHelper(window).Handle == IntPtr.Zero && PresentationSource.FromVisual(root) is null
                && ReferenceEquals(Keyboard.FocusedElement, initialFocus),
                "Node-search verification opens no native window and performs no native focus or Computer Use; native focus traversal is not exercised.");
        }
        finally
        {
            window.CloseNodeSearch(); workspace.CancelActiveGesture();
            root.Width = oldWidth; root.Height = oldHeight;
            await layout();
        }
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
