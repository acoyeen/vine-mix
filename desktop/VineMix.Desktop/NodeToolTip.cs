using System;
using System.Windows;
using System.Windows.Controls;

namespace VineMix.Desktop;

/// <summary>Short, scannable hover help. Longer tutorials belong in the workspace guide.</summary>
internal static class NodeToolTip
{
    internal const double ContentWidth = 300;

    public static StackPanel Create(string title, params string[] lines)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(lines);
        var content = new StackPanel { MaxWidth = ContentWidth, UseLayoutRounding = true };
        content.Children.Add(Line(title, title: true));
        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            content.Children.Add(Line(line, title: false));
        }
        return content;
    }

    private static TextBlock Line(string text, bool title)
    {
        var line = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 18,
            FontWeight = title ? FontWeights.SemiBold : FontWeights.Normal,
            Margin = new Thickness(0, title ? 0 : 5, 0, 0)
        };
        line.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
        return line;
    }
}
