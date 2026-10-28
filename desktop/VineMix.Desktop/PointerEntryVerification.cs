using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached entry-lifecycle checks; no native popup, input injection or audio.</summary>
internal static class PointerEntryVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        var controls = new[] { VerifyBlurCommit(require), VerifyInvalidRetention(require),
            VerifyContextMenu(require), VerifyWholeSamples(require), VerifyCancellation(require) };
        VerifyLocalizedEntry(require);
        var row = new WrapPanel();
        foreach (var control in controls) row.Children.Add(control);
        var host = new Border { Width = 720, Padding = new Thickness(16), Child = row,
            Background = ConsoleTheme.Get("ConsolePanelBrush") };
        host.Measure(new Size(host.Width, double.PositiveInfinity));
        host.Arrange(new Rect(0, 0, host.Width, host.DesiredSize.Height)); host.UpdateLayout();
        host.Height = host.DesiredSize.Height;
        require(controls.All(control => PresentationSource.FromVisual(control) is null
            && !Entry(control).IsKeyboardFocusWithin && !Entry(control).ContextMenu.IsOpen),
            "Pointer-entry regression checks stay detached without native focus, popup windows or injected pointer input; native focus timing remains unverified.");
        return host;
    }

    private static RotaryValueControl VerifyBlurCommit(Action<bool, string> require)
    {
        var edits = new List<double>();
        var control = new RotaryValueControl("Exact delay", 0, 1000, .001, "ms", 1, edits.Add, tile: true);
        var entry = Entry(control);
        const double precise = 1.23456789012345;
        control.SetValue(precise); Focus(entry);
        require(entry.Text == Text(precise, "ms") && !control.HasPendingEntry,
            "Entering a precise stored value exposes its round-trip number without creating a pending edit.");
        Blur(entry); Blur(entry);
        require(control.Value == precise && edits.Count == 0 && !control.HasPendingEntry,
            "Leaving an untouched precise field never commits its rounded resting readout or emits a redundant edit.");

        const double changed = 2.34567890123456;
        Focus(entry); entry.Text = Text(changed, "ms");
        require(control.HasPendingEntry && control.Value == precise && edits.Count == 0,
            "Typing a valid number leaves the saved value unchanged until the entry is applied.");
        Blur(entry); Blur(entry);
        require(control.Value == changed && edits.SequenceEqual(new[] { changed }) && !control.HasPendingEntry,
            "The actual LostKeyboardFocus route commits a valid exact draft once, including precision finer than the dial step.");
        Focus(entry); Blur(entry);
        require(control.Value == changed && edits.Count == 1,
            "Refocusing and leaving an unchanged precise value preserves all stored digits without another edit.");
        entry.Text = Text(3.456789, "ms");
        require(PressKey(entry, Key.Enter).Handled, "Enter still applies a valid draft on the actual key route.");
        Blur(entry);
        require(control.Value == 3.456789 && edits.SequenceEqual(new[] { changed, 3.456789 }),
            "Pointer focus loss after Enter cannot apply the same value twice.");
        return control;
    }

    private static RotaryValueControl VerifyInvalidRetention(Action<bool, string> require)
    {
        int edits = 0;
        var control = new RotaryValueControl("Level", -60, 12, .1, "dB", -6, _ => edits++, tile: true);
        var entry = Entry(control);
        var peer = new TextBoxAutomationPeer(entry);
        Size before = Arrange(control);
        foreach (string invalid in new[] { "invalid", "", "NaN", "Infinity", Text(12.1, "dB") })
        {
            Focus(entry); entry.Text = invalid; Blur(entry);
            require(entry.Text == invalid && control.Value == -6 && edits == 0 && control.HasPendingEntry
                && peer.GetItemStatus().Contains("Invalid value") && peer.GetHelpText().Contains("Not applied")
                && ReferenceEquals(entry.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush")),
                "Invalid pointer blur retains the rejected text, saved value and accessible validation error without applying audio changes.");
        }
        entry.Select(1, Math.Min(3, entry.Text.Length - 1));
        string draft = entry.Text; int selectionStart = entry.SelectionStart, selectionLength = entry.SelectionLength;
        control.SetValue(control.Value); Focus(entry);
        require(entry.Text == draft && entry.SelectionStart == selectionStart && entry.SelectionLength == selectionLength
            && peer.GetItemStatus().Contains("Invalid value") && edits == 0,
            "An unchanged model refresh and refocus preserve a rejected draft, its selection and accessible error.");
        bool? previousContrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            foreach (bool contrast in new[] { false, true })
            {
                ConsoleTheme.SetPreviewHighContrast(contrast); control.SetValue(control.Value);
                require(entry.Text == draft && control.HasPendingEntry && edits == 0
                    && peer.GetItemStatus().Contains("Invalid value")
                    && ReferenceEquals(entry.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush"))
                    && Arrange(control) == before && entry.ActualWidth == 110 && entry.ActualHeight == 28,
                    "Theme refresh preserves pending validation and updates its border without enlarging the existing value field or tile.");
            }
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previousContrast); }
        entry.Text = Text(-3.125, "dB"); Blur(entry);
        require(control.Value == -3.125 && edits == 1 && !control.HasPendingEntry
            && peer.GetItemStatus().Length == 0 && !peer.GetHelpText().Contains("Invalid value"),
            "Correcting a rejected value and clicking away applies it once and clears accessible validation.");
        entry.Text = "invalid"; Blur(entry); control.SetValue(-9.25);
        require(control.Value == -9.25 && edits == 1 && !control.HasPendingEntry
            && entry.Text != "invalid" && peer.GetItemStatus().Length == 0,
            "A changed model value replaces an obsolete invalid draft without sending another user edit.");
        entry.Text = "invalid"; Blur(entry);
        require(Arrange(control) == before, "Persistent invalid feedback adds no row or layout height.");
        return control;
    }

    private static RotaryValueControl VerifyContextMenu(Action<bool, string> require)
    {
        var edits = new List<double>();
        var control = new RotaryValueControl("Ratio", 1, 20, .1, ":1", 3.5, edits.Add, tile: true);
        var entry = Entry(control); var menu = entry.ContextMenu;
        var apply = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Apply value"));
        var cancel = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Cancel edit"));
        var commands = new[] { ApplicationCommands.Undo, ApplicationCommands.Cut, ApplicationCommands.Copy,
            ApplicationCommands.Paste, ApplicationCommands.SelectAll };
        require(commands.All(command => menu.Items.OfType<MenuItem>().Count(item => item.Command == command
            && ReferenceEquals(item.CommandTarget, entry)) == 1),
            "Undo, Cut, Copy, Paste and Select All in the numeric menu explicitly target their own text box.");
        OpenMenu(entry);
        require(!apply.IsEnabled && !cancel.IsEnabled,
            "Apply value and Cancel edit are unavailable when the value field has no pending edit.");
        CloseMenu(menu); DrainInput();

        Focus(entry); entry.Text = Text(4.25, ":1"); OpenMenu(entry); Blur(entry);
        require(apply.IsEnabled && cancel.IsEnabled && control.Value == 3.5 && edits.Count == 0 && control.HasPendingEntry,
            "ContextMenuOpening protects the draft before menu focus loss, so opening Cancel cannot first apply it.");
        cancel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); CloseMenu(menu); DrainInput(); Blur(entry);
        require(control.Value == 3.5 && edits.Count == 0 && !control.HasPendingEntry,
            "The actual Cancel edit menu action discards a valid draft without an accidental commit on menu closure or blur.");

        entry.Text = Text(5.125, ":1"); OpenMenu(entry); Blur(entry);
        apply.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); CloseMenu(menu); DrainInput(); Blur(entry);
        require(control.Value == 5.125 && edits.SequenceEqual(new[] { 5.125 }) && !control.HasPendingEntry,
            "The actual Apply value menu action commits once, and subsequent menu closure and blur emit no duplicate edit.");
        entry.Text = Text(6.25, ":1"); OpenMenu(entry); Blur(entry); CloseMenu(menu);
        require(control.Value == 5.125 && edits.Count == 1 && control.HasPendingEntry,
            "Menu Closed defers the focus outcome until WPF has finished its menu transition.");
        DrainInput();
        require(control.Value == 6.25 && edits.SequenceEqual(new[] { 5.125, 6.25 }) && !control.HasPendingEntry,
            "After a menu closes with focus outside the field, its pending valid draft is applied once.");

        entry.Text = Text(7.75, ":1"); OpenMenu(entry); Blur(entry); CloseMenu(menu); OpenMenu(entry); DrainInput();
        require(control.Value == 6.25 && edits.Count == 2 && control.HasPendingEntry,
            "A deferred close from an earlier menu cannot commit while the entry menu has reopened.");
        cancel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); CloseMenu(menu); DrainInput();
        entry.Text = "invalid"; Blur(entry); OpenMenu(entry); Blur(entry);
        cancel.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); CloseMenu(menu); DrainInput();
        require(control.Value == 6.25 && edits.Count == 2 && !control.HasPendingEntry
            && AutomationProperties.GetItemStatus(entry).Length == 0,
            "Cancel edit also clears a rejected draft and its accessible error without changing the saved value.");
        return control;
    }

    private static RotaryValueControl VerifyWholeSamples(Action<bool, string> require)
    {
        int edits = 0;
        var control = new RotaryValueControl("Samples", -48000, 48000, 1, "samples", 0, _ => edits++, tile: true, integer: true);
        var entry = Entry(control);
        entry.Text = Text(1.5, "samples"); Blur(entry);
        require(control.Value == 0 && edits == 0 && control.HasPendingEntry
            && AutomationProperties.GetItemStatus(entry).Contains("whole number")
            && entry.Text == Text(1.5, "samples"),
            "Pointer blur rejects fractional samples, retains the draft and exposes the whole-number requirement.");
        entry.Text = "48 samples"; Blur(entry); Blur(entry);
        require(control.Value == 48 && edits == 1 && !control.HasPendingEntry
            && AutomationProperties.GetItemStatus(entry).Length == 0,
            "Pointer blur accepts whole samples with their displayed unit and applies them only once.");
        return control;
    }

    private static RotaryValueControl VerifyCancellation(Action<bool, string> require)
    {
        int edits = 0;
        var control = new RotaryValueControl("Width / Q", .1, 20, .01, "", 1, _ => edits++, logarithmic: true, tile: true);
        var entry = Entry(control);
        control.SetValue(2); entry.Text = Text(3); control.CancelPendingEntry(); Blur(entry);
        require(control.Value == 2 && edits == 0 && !control.HasPendingEntry,
            "Explicit pending-entry cancellation restores the saved value without a later blur commit.");
        entry.Text = Text(4);
        require(PressKey(entry, Key.Escape).Handled, "Escape still cancels a pending value through the actual key route.");
        Blur(entry);
        require(control.Value == 2 && edits == 0 && !control.HasPendingEntry,
            "Escape followed by pointer blur cannot apply the cancelled value.");
        entry.Text = Text(5); control.SetValue(3); Blur(entry);
        require(control.Value == 3 && edits == 0 && !control.HasPendingEntry,
            "A changed model value supersedes a valid pending draft, preventing stale text from committing later.");

        entry.Text = Text(6); control.IsEnabled = false; Blur(entry);
        require(control.Value == 3 && edits == 0 && !control.HasPendingEntry,
            "Disabling a control discards its pending draft before blur without applying it.");
        entry.Text = Text(7); Blur(entry);
        require(!control.TryCommitText(Text(7)) && control.Value == 3 && edits == 0 && !control.HasPendingEntry,
            "Disabled entry rejects direct commits and removes any stale text on focus loss.");
        control.IsEnabled = true; entry.Text = "invalid"; Blur(entry);
        control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Blur(entry);
        require(control.Value == 3 && edits == 0 && !control.HasPendingEntry
            && AutomationProperties.GetItemStatus(entry).Length == 0,
            "Unloading cancels a rejected entry and its error without changing the saved parameter.");
        entry.Text = Text(8); control.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Blur(entry);
        require(control.Value == 3 && edits == 0 && !control.HasPendingEntry,
            "Unloading also discards a valid uncommitted value instead of applying it on a later blur.");
        entry.Text = Text(9); entry.IsReadOnly = true; Blur(entry);
        require(control.Value == 3 && edits == 0 && !control.HasPendingEntry,
            "A field made read-only cancels its draft on blur without a value change.");
        entry.IsReadOnly = false; entry.Text = "invalid"; Blur(entry);
        control.ResetToDefault(); control.ResetToDefault(); Blur(entry);
        require(control.Value == 1 && edits == 1 && !control.HasPendingEntry
            && AutomationProperties.GetItemStatus(entry).Length == 0,
            "Reset discards rejected text, restores the default exactly once and leaves no stale blur edit.");
        require(control.TryCommitPendingEntry() && edits == 1,
            "Applying with no pending draft succeeds without another callback.");
        return control;
    }

    private static void VerifyLocalizedEntry(Action<bool, string> require)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "fr-FR", "de-DE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                int edits = 0;
                var control = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, _ => edits++, tile: true);
                var entry = Entry(control);
                entry.Text = Text(-3.125, "dB").Replace('-', '−'); Blur(entry);
                require(control.Value == -3.125 && edits == 1 && !control.HasPendingEntry,
                    $"Pointer blur accepts {culture} decimal notation, a Unicode minus and the displayed unit without rounding.");
                Focus(entry); Blur(entry);
                require(control.Value == -3.125 && edits == 1,
                    $"An untouched exact value round-trips in {culture} after pointer focus changes.");
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    private static string Text(double value, string unit = "") => value.ToString("G", CultureInfo.CurrentCulture)
        + (unit.Length == 0 ? "" : (unit == ":1" ? "" : " ") + unit);
    private static TextBox Entry(DependencyObject control) => Descendants<TextBox>(control).Single();
    private static void Focus(TextBox entry) => entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, entry)
        { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
    private static void Blur(TextBox entry) => entry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, entry, null)
        { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
    private static Size Arrange(FrameworkElement control)
    {
        control.Measure(new Size(136, double.PositiveInfinity));
        control.Arrange(new Rect(new Point(), control.DesiredSize)); control.UpdateLayout();
        return control.RenderSize;
    }
    private static void OpenMenu(TextBox entry)
    {
        // WPF makes this event-args constructor internal. Reflection creates
        // only the framework arguments; the real routed event invokes the handler.
        var constructor = typeof(ContextMenuEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: new[] { typeof(object), typeof(bool) }, modifiers: null)
            ?? throw new InvalidOperationException("WPF context-menu event arguments are unavailable.");
        var args = (ContextMenuEventArgs)constructor.Invoke(new object[] { entry, true });
        args.RoutedEvent = ContextMenuService.ContextMenuOpeningEvent; entry.RaiseEvent(args);
    }
    private static void CloseMenu(ContextMenu menu) => menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
    private static void DrainInput()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static KeyEventArgs PressKey(UIElement target, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, new DetachedKeySource(), 0, key)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args); return args;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private sealed class DetachedKeySource : PresentationSource
    {
        public override Visual RootVisual { get; set; } = null!;
        public override bool IsDisposed => false;
        protected override CompositionTarget GetCompositionTargetCore() => null!;
    }
}
