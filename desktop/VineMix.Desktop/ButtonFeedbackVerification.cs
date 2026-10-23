using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Real button templates in detached trees: no native input, focus, window or application action.</summary>
internal static class ButtonFeedbackVerification
{
    private sealed class PressProbe : Button
    {
        internal void SetPressed(bool pressed) => IsPressed = pressed;
    }

    internal static FrameworkElement Run(MainWindow window, FrameworkElement root, Action<bool, string> require)
    {
        require(window.DataContext is MixerViewModel { IsPreview: true }
            && PresentationSource.FromVisual(root) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero,
            "Button feedback verification uses the detached preview without native focus or an audio connection.");
        var keyboardFocus = (Style)Application.Current.FindResource("ButtonKeyboardFocus");
        var mainButtons = Descendants<Button>(root).ToArray();
        require(mainButtons.Length > 10 && mainButtons.Contains(window.SettingsButton)
            && mainButtons.Contains(window.LayoutLockButton) && mainButtons.Contains(window.WebsiteButton)
            && mainButtons.Any(button => AutomationProperties.GetName(button) == "Maximise window"),
            "The shared button audit includes real settings, padlock, website, caption and node controls.");
        foreach (var button in mainButtons) Audit(button, keyboardFocus, require);

        var about = MainWindow.CreateAboutContent(() => { }, _ => { });
        var whatsNew = MainWindow.CreateWhatsNewContent([], () => { });
        var search = new NodeSearchView(_ => false, () => { });
        var help = new WorkspaceHelpView(() => { });
        using var lesson = HelpControlLessons.Buttons();
        foreach (var surface in new[] { about, whatsNew, search, help, lesson.View })
        {
            Measure(surface, 780, 700);
            var buttons = Descendants<Button>(surface).ToArray();
            require(buttons.Length > 0 && PresentationSource.FromVisual(surface) is null,
                "Detached dialogs, search and Help expose their actual button templates without opening a surface.");
            foreach (var button in buttons) Audit(button, keyboardFocus, require);
        }
        var reference = Descendants<Button>(help).First(button => !ReferenceEquals(button, help.CloseButton)
            && !ReferenceEquals(button, help.ResetButton) && button.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue);
        var stateButton = ((HelpControlLessons.ButtonsLesson)lesson).BypassButton;
        stateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(((HelpControlLessons.ButtonsLesson)lesson).Bypassed
            && SameBrush(stateButton.Background, ConsoleTheme.Get("ConsoleMuteBrush")),
            "The real isolated Bypass lesson retains its visible on state after activation.");

        var rows = new StackPanel();
        var gallery = new Border { Width = 580, Height = 380, Padding = new Thickness(16), Child = rows };
        // Application palette invalidation reaches logical Window trees. As in
        // the main detached renderer, own the fixture without creating an HWND.
        // Press triggers re-resolve brushes; a persistent focus overlay must
        // receive the ordinary resource notification too.
        var galleryOwner = new Window { Content = gallery };
        gallery.SetResourceReference(Border.BackgroundProperty, "ConsolePanelBrush");
        var probes = new List<(PressProbe Button, string Kind)>();
        foreach ((object key, string label) in new (object, string)[]
        {
            (typeof(Button), "Standard action"), ("WorkspaceToolbarButton", "Toolbar action"),
            ("PrimaryWorkspaceToolbarButton", "Primary action"), ("BrandWebsiteButton", "Website action")
        })
        {
            var button = new PressProbe { Content = label, Style = (Style)Application.Current.FindResource(key),
                MinWidth = 140, Margin = new Thickness(0, 4, 0, 4) };
            probes.Add((button, label)); rows.Children.Add(button);
        }
        foreach ((Button source, string label) in new[] { (reference, "Help reference"), (stateButton, "State action") })
        {
            var button = new PressProbe { Content = label, Style = source.Style, Template = source.Template,
                BorderThickness = source.BorderThickness, Margin = new Thickness(0, 4, 0, 4) };
            button.SetResourceReference(Control.BackgroundProperty, label == "State action" ? "ConsoleMuteBrush" : "ConsoleButtonFaceBrush");
            button.SetResourceReference(Control.BorderBrushProperty, label == "State action" ? "ConsoleMuteBrush" : "ConsoleControlBorderBrush");
            if (label == "State action") button.SetResourceReference(Control.ForegroundProperty, "ConsoleInkBrush");
            probes.Add((button, label)); rows.Children.Add(button);
        }
        var focusPreview = new Grid { Height = 34, Margin = new Thickness(0, 8, 0, 0) };
        focusPreview.Children.Add(new Button { Content = "Keyboard navigation cue" });
        var focusAdorner = new Control { Style = keyboardFocus, IsHitTestVisible = false };
        focusPreview.Children.Add(focusAdorner); rows.Children.Add(focusPreview);
        bool? previousContrast = ConsoleTheme.PreviewHighContrast;
        try
        {
            foreach (bool highContrast in new[] { false, true })
            {
                ConsoleTheme.SetPreviewHighContrast(highContrast);
                Measure(gallery, 580, 380);
                foreach ((PressProbe button, string kind) in probes)
                {
                    int clicks = 0;
                    RoutedEventHandler clicked = (_, _) => clicks++;
                    button.Click += clicked;
                    try
                    {
                        Audit(button, keyboardFocus, require);
                        var face = (Border)button.Template.FindName("Face", button);
                        var originalFill = face.Background;
                        button.SetPressed(true); button.UpdateLayout();
                        require(button.IsPressed && !BottomOnly(face.BorderThickness),
                            kind + " has no bottom-only decoration while pressed, including in high contrast.");
                        if (kind is "Standard action" or "Toolbar action" or "Help reference")
                        {
                            string resource = kind == "Standard action" ? "ConsoleWellBrush" : "ConsoleControlBorderBrush";
                            require(SameBrush(face.Background, ConsoleTheme.Get(resource)),
                                kind + " retains its existing pressed background.");
                        }
                        if (kind == "State action")
                            require(SameBrush(face.Background, originalFill) && button.Opacity == .8,
                                "State buttons retain their checked-state fill while pressed.");
                        button.SetPressed(false);
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        button.UpdateLayout();
                        require(clicks == 1 && !button.IsPressed && SameBrush(face.Background, originalFill)
                            && !BottomOnly(face.BorderThickness),
                            kind + " releases its press feedback without a persistent underline and remains clickable.");
                    }
                    finally { button.SetPressed(false); button.Click -= clicked; }
                }
                var cue = Descendants<Border>(focusAdorner).Single();
                require(cue.BorderThickness == new Thickness(1) && cue.Margin == new Thickness(2)
                    && !cue.IsHitTestVisible && SameBrush(cue.BorderBrush, ConsoleTheme.Get("ConsoleFocusBrush")),
                    "The keyboard-only focus adorner is an inset one-pixel outline that follows normal and high-contrast colours. "
                    + $"High contrast={highContrast}; thickness={cue.BorderThickness}; margin={cue.Margin}; "
                    + $"hit testing={cue.IsHitTestVisible}; brush={cue.BorderBrush}; expected={ConsoleTheme.Get("ConsoleFocusBrush")}.");
            }
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previousContrast); }
        Measure(gallery, 580, 380);
        require(PresentationSource.FromVisual(gallery) is null && new WindowInteropHelper(window).Handle == IntPtr.Zero
            && new WindowInteropHelper(galleryOwner).Handle == IntPtr.Zero,
            "Button press and focus-cue checks finish without showing, focusing or activating any native window.");
        return gallery;
    }

    private static void Audit(Button button, Style keyboardFocus, Action<bool, string> require)
    {
        button.ApplyTemplate();
        string name = AutomationProperties.GetName(button);
        if (name.Length == 0) name = button.Content as string ?? button.GetType().Name;
        require(button.Template is not null && !button.Template.Triggers.Cast<TriggerBase>().Any(UsesGeneralFocus)
            && Styles(button.Style).All(style => !style.Triggers.Cast<TriggerBase>().Any(UsesGeneralFocus)),
            name + " has no general-focus trigger that could leave click feedback behind.");
        require(!Descendants<Border>(button).Any(border => BottomOnly(border.BorderThickness)),
            name + " contains no bottom-only button border.");
        // Sockets draw their own connection/focus shape and must retain it.
        if (button is not GraphWorkspace.Socket)
            require(ReferenceEquals(button.FocusVisualStyle, keyboardFocus) && button.Focusable && button.IsTabStop,
                name + " retains keyboard navigation and the shared keyboard-only focus cue.");
    }

    private static bool UsesGeneralFocus(TriggerBase trigger) => trigger switch
    {
        Trigger value => FocusProperty(value.Property),
        MultiTrigger value => value.Conditions.Cast<System.Windows.Condition>().Any(condition => FocusProperty(condition.Property)),
        _ => false
    };
    private static bool FocusProperty(DependencyProperty? property) => property == UIElement.IsKeyboardFocusedProperty
        || property == UIElement.IsFocusedProperty || property == UIElement.IsKeyboardFocusWithinProperty;
    private static bool BottomOnly(Thickness thickness) => thickness.Bottom > 0
        && thickness.Left == 0 && thickness.Top == 0 && thickness.Right == 0;
    private static bool SameBrush(Brush? first, Brush? second) => ReferenceEquals(first, second)
        || first is SolidColorBrush a && second is SolidColorBrush b && a.Color == b.Color;
    private static IEnumerable<Style> Styles(Style? style)
    {
        for (; style is not null; style = style.BasedOn) yield return style;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found) yield return found;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Measure(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }
}
