using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace AudioMixer.Desktop;

/// <summary>A scrollable reference surface; its host owns opening, closing and focus restoration.</summary>
internal sealed class WorkspaceHelpView : UserControl
{
    internal Button CloseButton { get; }
    internal ScrollViewer ScrollViewer { get; }

    internal WorkspaceHelpView(Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        FontSize = 13;
        UseLayoutRounding = SnapsToDevicePixels = true;
        SetResourceReference(FontFamilyProperty, "NodeFontFamily");
        SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        SetResourceReference(BackgroundProperty, "ConsolePanelBrush");
        AutomationProperties.SetName(this, "Help & shortcuts");

        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        Content = root;

        var header = new Grid { Margin = new Thickness(20, 18, 20, 12) };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Text("Help & shortcuts", 21, FontWeights.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(0, 0, 16, 0);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        header.Children.Add(title);
        CloseButton = new Button
        {
            Content = "Close", MinWidth = 68, MinHeight = 30,
            VerticalAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12, 4, 12, 4), ToolTip = "Close help (Esc)"
        };
        AutomationProperties.SetName(CloseButton, "Close help");
        KeyboardNavigation.SetTabIndex(CloseButton, 0);
        CloseButton.Click += (_, _) => close();
        Grid.SetColumn(CloseButton, 1); header.Children.Add(CloseButton);
        root.Children.Add(header);

        var content = new StackPanel { Margin = new Thickness(20, 0, 20, 20) };
        ScrollViewer = new ScrollViewer
        {
            Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false, PanningMode = PanningMode.VerticalOnly,
            Focusable = true, BorderThickness = new Thickness(0)
        };
        KeyboardNavigation.SetIsTabStop(ScrollViewer, true);
        KeyboardNavigation.SetTabIndex(ScrollViewer, 1);
        AutomationProperties.SetName(ScrollViewer, "Shortcut reference");
        AutomationProperties.SetHelpText(ScrollViewer, "Use the arrow keys or Page Up and Page Down to scroll this reference.");
        Grid.SetRow(ScrollViewer, 1); root.Children.Add(ScrollViewer);

        var introduction = Text("View and Edit keys apply to the canvas. Equalizer keys apply when its plot is focused. Dial keys apply when a dial is focused.");
        introduction.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        introduction.Margin = new Thickness(0, 0, 0, 8);
        content.Children.Add(introduction);

        AddSection(content, "View",
            ("Ctrl+F", "Find a node by name, type, device or app"),
            ("F", "Frame selected nodes"),
            ("Home", "Fit all nodes"),
            ("Ctrl+0", "Return to 100% zoom"),
            ("Mouse wheel", "Zoom the canvas"),
            ("Middle drag / Space+drag", "Pan the canvas"),
            ("Right drag", "Pan empty canvas when Right-click drag to pan is enabled in Settings."));
        AddSection(content, "Edit",
            ("Shift+A", "Add a node"),
            ("Ctrl+A", "Select all nodes"),
            ("Ctrl+C / Ctrl+V", "Copy / paste selected nodes"),
            ("Alt+X / Alt+Y", "Align selected nodes in connection order in a spaced horizontal row / vertical column"),
            ("Alt+A", "Auto Align selected nodes using their current layout"),
            ("Right-click selection", "Align selected nodes → Left edges / Top edges / Auto Align"),
            ("Ctrl+Z", "Undo"),
            ("Ctrl+Y / Ctrl+Shift+Z", "Redo"),
            ("Delete", "Remove the selected nodes or connection"),
            ("Escape", "Cancel the current gesture or connection"));
        AddSection(content, "Dials",
            ("Drag up / down", "Adjust the value; hold Shift for fine adjustment"),
            ("Click the value", "Type an exact value. Enter applies; Escape cancels."),
            ("Tab / Shift+Tab", "Apply a valid value and move to the next / previous control."),
            ("Double-click / Home", "Reset to the default value"),
            ("Arrow keys", "Adjust the focused dial"),
            ("Page Up / Page Down", "Make larger adjustments"));

        AddSection(content, "Equalizer",
            ("Ctrl+click a point", "Toggle the band in the selection"),
            ("Drag empty plot space", "Box-select bands; hold Ctrl to add to the selection"),
            ("Ctrl+A", "Select all bands"),
            ("Drag a selected point / Arrow keys", "Move the selected bands together; hold Shift for fine movement"),
            ("Home / Delete", "Reset selected band positions / remove selected bands"),
            ("Escape", "Cancel the selection box"),
            ("Double-click empty plot space", "Add a bell band"),
            ("Double-click a point", "Reset its frequency and gain, keeping its shape and width"),
            ("Dials below the plot", "Edit Frequency, Band gain and Width / Q for the active band only"));

        var footer = Text("Closing the window hides it in the tray; audio keeps running. Double-click the tray icon to return.");
        footer.Margin = new Thickness(0, 20, 0, 8);
        footer.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        content.Children.Add(footer);
        var quit = Text("To stop audio, choose Quit Audio Mixer from the tray menu.");
        quit.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        content.Children.Add(quit);
    }

    private static void AddSection(Panel parent, string heading, params (string Keys, string Action)[] rows)
    {
        var title = Text(heading, 16, FontWeights.SemiBold);
        title.Margin = new Thickness(0, 14, 0, 6);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        parent.Children.Add(title);
        var table = new Grid();
        table.ColumnDefinitions.Add(new() { Width = new GridLength(2, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new() { Width = new GridLength(3, GridUnitType.Star) });
        foreach (var row in rows)
        {
            int index = table.RowDefinitions.Count;
            table.RowDefinitions.Add(new() { Height = GridLength.Auto });
            var keys = Text(row.Keys, weight: FontWeights.SemiBold);
            keys.Margin = new Thickness(0, 5, 18, 5);
            var action = Text(row.Action);
            action.Margin = new Thickness(0, 5, 0, 5);
            Grid.SetRow(keys, index); table.Children.Add(keys);
            Grid.SetRow(action, index); Grid.SetColumn(action, 1); table.Children.Add(action);
        }
        parent.Children.Add(table);
    }

    private static TextBlock Text(string text, double size = 13, FontWeight? weight = null)
        => new()
        {
            Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top
        };
}
