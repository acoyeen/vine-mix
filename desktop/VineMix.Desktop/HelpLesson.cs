using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace VineMix.Desktop;

/// <summary>Owns only disposable, in-memory practice state.</summary>
internal interface IHelpLesson : IDisposable
{
    FrameworkElement View { get; }
    void Reset();
    bool TryCancel();
    void Suspend();
}

internal static class HelpLessonUi
{
    internal static TextBlock Text(string text, double size = 13, bool bold = false)
    {
        var label = new TextBlock { Text = text, FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        label.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
        return label;
    }

    internal static TextBlock Hint(string text)
    {
        var label = Text(text);
        label.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        return label;
    }

    internal static StackPanel Section(string title, string description, params UIElement[] children)
    {
        var body = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        if (title.Length > 0)
        {
            var heading = Text(title, 13, true);
            heading.Margin = new Thickness(0, 0, 0, 8);
            AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level3);
            body.Children.Add(heading);
        }
        if (description.Length > 0) body.Children.Add(Hint(description));
        foreach (var child in children) body.Children.Add(child);
        return body;
    }

    internal static Button Button(string text, Action click, string? help = null)
    {
        var button = new Button { Content = text, MinHeight = 26, Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 8, 6), HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = help ?? text };
        button.SetResourceReference(FrameworkElement.StyleProperty, "WorkspaceToolbarButton");
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }
}
