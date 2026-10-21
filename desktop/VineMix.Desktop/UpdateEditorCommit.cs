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
    internal static bool TryCommit(DependencyObject? focused)
    {
        for (var current = focused; current is not null; current = Parent(current))
            if (current is RotaryValueControl dial) return dial.TryCommitPendingEntry();
        if (focused is TextBox text)
        {
            text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            return !Validation.GetHasError(text);
        }
        return true;
    }
    private static DependencyObject? Parent(DependencyObject value)
        => value is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(value) : LogicalTreeHelper.GetParent(value);
}
