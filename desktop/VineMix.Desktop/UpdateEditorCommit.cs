using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal static class UpdateEditorCommit
{
    internal static bool TryCommitFocused() => TryCommit(Keyboard.FocusedElement as DependencyObject);
    internal static bool TryCommitWorkspace(DependencyObject root)
    {
        if (!TryCommitFocused()) return false;
        // A rejected value can remain visible after pointer focus moves away.
        // Snapshot this workspace's pending editors before a successful commit
        // refreshes the graph; Help examples and other windows stay outside it.
        var pending = new List<DependencyObject>();
        CollectPending(root, pending);
        foreach (var editor in pending)
            if (!TryCommitEditor(editor)) return false;
        return true;
    }

    internal static bool TryCommit(DependencyObject? focused)
    {
        for (var current = focused; current is not null; current = Parent(current))
            if (current is RotaryValueControl or GraphFrameView { IsRenaming: true } or GraphNodeView { HasPendingRename: true })
                return TryCommitEditor(current);
        if (focused is TextBox text)
        {
            if (text.IsEnabled && !text.IsReadOnly)
                text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            return !Validation.GetHasError(text);
        }
        return true;
    }

    private static bool TryCommitDial(RotaryValueControl dial)
        => dial.IsEnabled ? dial.TryCommitPendingEntry() : !dial.HasPendingEntry;

    private static bool TryCommitEditor(DependencyObject editor) => editor switch
    {
        RotaryValueControl dial => TryCommitDial(dial),
        GraphFrameView frame => frame.TryCommitPendingRename(),
        GraphNodeView node => node.TryCommitPendingRename(),
        _ => true
    };

    private static void CollectPending(DependencyObject root, List<DependencyObject> pending)
    {
        if (root is RotaryValueControl dial)
        {
            if (dial.HasPendingEntry) pending.Add(dial);
            return;
        }
        // Name fields deliberately use callbacks rather than bindings. Commit
        // them before preparation disables editing and cancels their views.
        if (root is GraphFrameView { IsRenaming: true } or GraphNodeView { HasPendingRename: true }) pending.Add(root);
        if (root is not Visual and not System.Windows.Media.Media3D.Visual3D) return;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            CollectPending(VisualTreeHelper.GetChild(root, index), pending);
    }

    private static DependencyObject? Parent(DependencyObject value)
        => value is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
}
