using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace VineMix.Desktop;

/// <summary>
/// Exercises every real Add-node row with detached geometry and the production
/// menu help controller. Does not open popups, move native focus or use an engine.
/// </summary>
internal static class AddNodeHelpVerification
{
    internal static async Task RunAsync(Action<bool, string> require)
    {
        IInputElement? initialFocus = Keyboard.FocusedElement;
        string[] kinds = NodeCatalog.Categories.SelectMany(category => category.Kinds).ToArray();
        require(kinds.Length == kinds.Distinct().Count() && kinds.ToHashSet().SetEquals(GraphDefaults.Kinds.Where(NodeCatalog.CanCreate))
            && kinds.All(kind => !string.IsNullOrWhiteSpace(NodeCatalog.Description(kind)))
            && NodeCatalog.Categories.All(category => !string.IsNullOrWhiteSpace(category.Description)),
            "Every creatable node and Add category has a catalog purpose; legacy-only nodes stay outside the picker.");
        using (var mixer = new MixerViewModel(preview: true))
        {
            var snapshot = UiPreview.CreateSnapshot([]); snapshot.Configuration.Graph = new();
            mixer.ApplyPreviewSnapshot(snapshot);
            await VerifyMenu(mixer, kinds, unavailable: "", require);
            snapshot = UiPreview.CreateSnapshot([]);
            snapshot.Configuration.Graph = new() { Nodes = Enumerable.Range(0, 32)
                .Select(index => { var node = GraphDefaults.NewNode("mix", index * 8, 0); node.Id = "preview-picker-limit-" + index; return node; }).ToList() };
            mixer.ApplyPreviewSnapshot(snapshot);
            await VerifyMenu(mixer, kinds, unavailable: "limit of 32 nodes", require);
        }
        // No preview snapshot means no connection; this fixture does not start
        // a connection attempt or touch recovery/settings files.
        using (var waiting = new MixerViewModel(preview: true))
            await VerifyMenu(waiting, kinds, unavailable: "Wait for the audio engine", require);
        require(ReferenceEquals(Keyboard.FocusedElement, initialFocus),
            "Complete Add-picker help coverage leaves native keyboard focus unchanged; physical keyboard traversal is not simulated.");
    }

    private static async Task VerifyMenu(MixerViewModel mixer, string[] kinds, string unavailable, Action<bool, string> require)
    {
        string baseline = State(mixer);
        var workspace = new GraphWorkspace(mixer, useSystemClipboard: false);
        var menu = workspace.CreateAddMenu(new Point(24, 24));
        int clicks = 0;
        foreach (var row in Rows(menu)) row.Click += (_, _) => clicks++;
        var groups = menu.Items.OfType<MenuItem>().Where(row => row.HasItems).ToArray();
        var flat = menu.Items.OfType<MenuItem>().Where(row => row.Icon is not null && !row.HasItems).ToArray();
        var searchRow = menu.Items.OfType<MenuItem>().First();
        var search = ((Grid)searchRow.Header).Children.OfType<TextBox>().Single();
        using var pointer = new Pointer(menu, require);
        await Arrange(menu);
        require(mixer.IsPreview && !menu.IsOpen && PresentationSource.FromVisual(workspace) is null
            && groups.Length == NodeCatalog.Categories.Count && flat.Length == kinds.Length,
            "The real Add menu uses an isolated preview and exposes both a category row and a search-result row for every node kind.");
        var helpObjects = groups.SelectMany(group => group.Items.OfType<MenuItem>()).Concat(flat).Select(row => row.ToolTip).ToArray();
        require(helpObjects.Distinct().Count() == 2 * kinds.Length,
            "Every categorized and filtered node row owns an independent help panel rather than sharing an already-parented visual.");
        require(search.ToolTip is null && AutomationProperties.GetHelpText(search).Length > 0
            && AutomationProperties.GetHelpText(searchRow) == AutomationProperties.GetHelpText(search),
            "The Add search editor and its owning menu wrapper expose the same accessible explanation, with one menu-owned tooltip.");
        pointer.Show(searchRow, "Add search");

        foreach (var category in NodeCatalog.Categories)
        {
            var group = groups.Single(row => Equals(row.Header, category.Title));
            CheckHelp(group, category.Title, category.Description, "", require);
            pointer.Show(group, category.Title + " category");
            group.ApplyTemplate();
            var popup = group.Template.FindName("PART_Popup", group) as Popup;
            require(popup?.Child is FrameworkElement && !popup.IsOpen && !group.IsSubmenuOpen,
                category.Title + " uses its real submenu template without opening the popup.");
            if (popup?.Child is not FrameworkElement surface) continue;
            await ArrangeSubmenu(surface);
            foreach (string kind in category.Kinds)
            {
                var row = group.Items.OfType<MenuItem>().Single(item => Equals(item.Header, NodeCatalog.Title(kind)));
                CheckHelp(row, NodeCatalog.Title(kind), NodeCatalog.Description(kind), unavailable, require);
                row.BringIntoView(); await Flush(); surface.UpdateLayout();
                pointer.Show(row, NodeCatalog.Title(kind) + " categorized");
                if (row.IsEnabled) pointer.CheckKeyboardOwner(row, searchRow, NodeCatalog.Title(kind));
            }
            require(!popup.IsOpen && !group.IsSubmenuOpen && PresentationSource.FromVisual(surface) is null,
                category.Title + " submenu checks leave its actual presentation surface detached and closed.");
        }
        foreach (string kind in kinds)
        {
            search.Text = kind; await Arrange(menu);
            var row = flat.Single(item => Equals(item.Header, NodeCatalog.Title(kind)));
            require(groups.All(group => group.Visibility == Visibility.Collapsed) && row.Visibility == Visibility.Visible,
                NodeCatalog.Title(kind) + " can be found in the actual filtered Add list.");
            CheckHelp(row, NodeCatalog.Title(kind), NodeCatalog.Description(kind), unavailable, require);
            row.BringIntoView(); await Flush(); menu.UpdateLayout();
            pointer.Show(row, NodeCatalog.Title(kind) + " filtered");
            pointer.CheckCollapsedRows(groups, row, NodeCatalog.Title(kind));
            if (row.IsEnabled) pointer.CheckKeyboardOwner(row, searchRow, NodeCatalog.Title(kind) + " filtered");
        }
        search.Text = "no-picker-kind-has-this-name"; await Arrange(menu);
        var empty = menu.Items.OfType<MenuItem>().Single(row => Equals(row.Header, "No matching nodes"));
        require(empty.Visibility == Visibility.Visible && !empty.IsEnabled && ToolTipService.GetShowOnDisabled(empty)
            && AutomationProperties.GetHelpText(empty).Contains("clear the search", StringComparison.Ordinal),
            "The disabled no-results row explains how to recover through both hover help and accessibility text.");
        pointer.Show(empty, "No matching nodes");
        search.Text = ""; await Arrange(menu);
        require(groups.All(group => group.Visibility == Visibility.Visible) && flat.All(row => row.Visibility == Visibility.Collapsed),
            "Clearing the Add query restores the original categories and retains their individual help.");
        require(clicks == 0 && State(mixer) == baseline && !menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "All Add-menu purpose, hover, filtering and keyboard-owner checks leave audio, preferences, save state and history untouched.");
    }

    private static void CheckHelp(MenuItem row, string title, string purpose, string unavailable, Action<bool, string> require)
    {
        var text = (row.ToolTip as StackPanel)?.Children.OfType<TextBlock>().Select(block => block.Text).ToArray();
        string accessible = new MenuItemAutomationPeer(row).GetHelpText();
        require(text is { Length: >= 2 } && text[0] == title && text[1] == purpose
            && AutomationProperties.GetName(row) == title && accessible.Contains(purpose, StringComparison.Ordinal)
            && !ToolTipService.GetIsEnabled(row),
            title + " has its own titled purpose in the shared menu tooltip and the accessible automation peer.");
        require(unavailable.Length == 0 ? row.IsEnabled && row.Focusable
            : !row.IsEnabled && ToolTipService.GetShowOnDisabled(row)
                && accessible.Contains(unavailable, StringComparison.Ordinal)
                && text!.Any(line => line.Contains(unavailable, StringComparison.Ordinal)),
            title + (unavailable.Length == 0 ? " remains keyboard focusable without requiring a hover." : " remains disabled while explaining why it cannot be added."));
    }

    private sealed class Pointer : IDisposable
    {
        private readonly ContextMenu _menu;
        private readonly Action<bool, string> _require;
        private (Visual Surface, Point Position)? _point;
        private bool _active = true, _open;
        private long _now = 1000;
        private MenuItem? _shown;
        private object? _content;
        private readonly MenuHoverHelp _controller;

        internal Pointer(ContextMenu menu, Action<bool, string> require)
        {
            _menu = menu; _require = require;
            _controller = MenuHoverHelp.AttachForVerification(menu, new(
                () => _active, () => _point, () => _now,
                row => row.ActualWidth > 0 && row.ActualHeight > 0 && row.Visibility == Visibility.Visible,
                () => _open, row => { _shown = row; _content = row.ToolTip; _open = true; },
                () => { _shown = null; _content = null; _open = false; }));
        }

        private void PointAt(FrameworkElement element)
        {
            Visual surface = element;
            while (VisualTreeHelper.GetParent(surface) is Visual parent) surface = parent;
            _point = (surface, element.TransformToAncestor(surface).Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2)));
        }

        internal void Show(MenuItem row, string label)
        {
            Reset(); PointAt(row);
            var actual = _point is { } point ? _controller.RowAt(point.Surface, point.Position) : null;
            _require(ReferenceEquals(actual, row),
                label + " is the real hit-tested row, including its content and padding."
                    + (ReferenceEquals(actual, row) ? "" : $" Expected {row.Header}; hit {actual?.Header ?? "no row"}; point {_point?.Position}; row {row.ActualWidth}x{row.ActualHeight} hit={row.IsHitTestVisible}; menu {_menu.ActualWidth}x{_menu.ActualHeight} hit={_menu.IsHitTestVisible}; surface {_point?.Surface.GetType().Name}; raw hit {(_point is { } sample ? VisualTreeHelper.HitTest(sample.Surface, sample.Position)?.VisualHit.GetType().Name : "none")}."));
            row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
            _require(ReferenceEquals(_controller.Owner, row) && _controller.IsPending,
                label + " enters the production menu hover-help timer through its actual routed callback.");
            _now = Math.Max(_now, _controller.PendingAt); _controller.ShowPending();
            _require(_open && ReferenceEquals(_shown, row) && ReferenceEquals(_content, row.ToolTip)
                && !_controller.Popup.IsOpen && PresentationSource.FromVisual(_controller.Popup) is null,
                label + " presents its own explanation through the detached recorder without a native popup.");
        }

        internal void CheckKeyboardOwner(MenuItem row, MenuItem pointerRow, string label)
        {
            Reset(); PointAt(pointerRow);
            _controller.RequestKeyboardHelp(row);
            _require(ReferenceEquals(_controller.Owner, row) && _controller.IsPending && !_open
                && row.Focusable && row.IsEnabled && new MenuItemAutomationPeer(row).GetHelpText().Length > 0,
                label + " schedules keyboard help independently of the pointer and exposes its purpose through accessibility.");
            // Native focus is deliberately absent. The production guard must
            // reject presenting keyboard help for a row that is not focused.
            _now = Math.Max(_now, _controller.PendingAt); _controller.ShowPending();
            _require(!_open && !_controller.IsPending && _controller.Owner is null,
                label + " does not present keyboard help when the detached row lacks actual keyboard focus.");
        }

        internal void CheckCollapsedRows(IEnumerable<MenuItem> collapsed, MenuItem visible, string label)
        {
            PointAt(visible);
            _require(collapsed.All(row => row.Visibility == Visibility.Collapsed)
                && _point is { } point && ReferenceEquals(_controller.RowAt(point.Surface, point.Position), visible),
                label + " remains the geometric owner after filtering, even when collapsed category visuals retain their old arrangement.");
            foreach (var hidden in collapsed) _controller.RequestPointerHelp(hidden);
            _require(_controller.Owner is null && !_controller.IsPending,
                "Collapsed Add categories cannot retain or acquire tooltip ownership over a visible filtered result.");
        }

        private void Reset()
        {
            _active = false; _menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent, _menu));
            _point = null; _now += 1000; _active = true;
        }

        public void Dispose()
        {
            Reset(); _active = false;
            _require(!_controller.IsPending && !_controller.Popup.IsOpen && !_open
                && PresentationSource.FromVisual(_menu) is null,
                "The Add help fixture releases all owners and timers while every real popup remains closed.");
        }
    }

    private static async Task Arrange(ContextMenu menu)
    {
        menu.ApplyTemplate(); menu.Height = double.NaN;
        menu.InvalidateMeasure(); menu.InvalidateArrange();
        menu.Measure(new Size(300, 520)); menu.Height = Math.Min(520, Math.Max(80, menu.DesiredSize.Height));
        menu.Arrange(new Rect(0, 0, 300, menu.Height)); menu.UpdateLayout();
        await Flush(); menu.UpdateLayout();
    }

    private static async Task ArrangeSubmenu(FrameworkElement surface)
    {
        surface.Measure(new Size(360, 526));
        surface.Arrange(new Rect(0, 0, Math.Max(210, surface.DesiredSize.Width), Math.Min(526, surface.DesiredSize.Height)));
        surface.UpdateLayout(); await Flush(); surface.UpdateLayout();
    }

    private static async Task Flush() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);

    private static IEnumerable<MenuItem> Rows(ItemsControl parent)
    {
        foreach (var row in parent.Items.OfType<MenuItem>())
        {
            yield return row;
            foreach (var child in Rows(row)) yield return child;
        }
    }

    private static string State(MixerViewModel mixer) => JsonSerializer.Serialize(new
    {
        mixer.GraphConfig, Preferences = mixer.SerializeUiPreferences(), mixer.ConfigurationPresentationCount,
        mixer.PendingEditCount, mixer.SaveStatus, History = mixer.EditHistory.Entries
    }, Protocol.Json);
}
