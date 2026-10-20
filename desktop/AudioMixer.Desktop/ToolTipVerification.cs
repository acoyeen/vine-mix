using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AudioMixer.Desktop.Controls;

namespace AudioMixer.Desktop;

/// <summary>Detached rendering of the actual tooltip template; never opens a popup.</summary>
internal static class ToolTipVerification
{
    internal static FrameworkElement Run(Action<bool, string> require)
    {
        var panel = new StackPanel { Margin = new Thickness(18) };
        var root = new Border { Width = 380, Child = panel };
        var tips = new System.Collections.Generic.List<ToolTip>();
        root.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        ToolTip Add(object content)
        {
            // ToolTip forbids a logical or visual parent. Lay out the actual
            // control independently, then paint it without opening its popup.
            var tip = new ToolTip { Content = content };
            tip.Measure(new Size(326, double.PositiveInfinity));
            tip.Arrange(new Rect(new Point(), tip.DesiredSize));
            tip.UpdateLayout();
            tips.Add(tip);
            panel.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = tip.ActualWidth, Height = tip.ActualHeight,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 16),
                Fill = new VisualBrush(tip) { AutoLayoutContent = false, Stretch = Stretch.None }
            });
            return tip;
        }
        var level = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, _ => { });
        Add(Descendants<Slider>(level).Single().ToolTip);
        var eq = new EqualizerEditor();
        Add(eq.ToolTip);
        eq.Apply([new EqBand { Type = "lowShelf", FrequencyHz = 180, GainDb = -3, Q = .7 }], false, 0);
        Add(eq.ToolTip);
        var voice = new VoiceColorPad();
        Add(voice.ToolTip);
        Add(new StereoNodeMeter().ToolTip);
        Add(new MeterPeakDisplay(new StereoNodeMeter()).ToolTip);
        const string error = "The selected USB microphone is unavailable. Reconnect the device or choose another microphone in this node. Your saved selection has been kept.";
        var diagnostic = Add(error);
        root.Measure(new Size(380, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, 380, root.DesiredSize.Height));
        root.UpdateLayout();
        root.Height = root.DesiredSize.Height;
        require(tips.All(tip => !tip.IsOpen && PresentationSource.FromVisual(tip) is null
            && VisualTreeHelper.GetParent(tip) is null && LogicalTreeHelper.GetParent(tip) is null),
            "Tooltip previews use the real template without opening popups or creating desktop windows.");
        require(tips.All(tip => tip.ActualWidth <= 326 && tip.ActualWidth > 40),
            "Structured and plain-message tooltips both fit the narrow popup width.");
        var diagnosticText = Descendants<TextBlock>(diagnostic).FirstOrDefault(block => block.Text == error);
        require(diagnosticText is not null && diagnosticText.TextWrapping == TextWrapping.Wrap && diagnosticText.ActualHeight > 36,
            "A full device-error message wraps over multiple lines without being shortened or clipped to one line.");
        require(eq.ToolTip is StackPanel eqContent && eqContent.Children.OfType<TextBlock>().All(block => block.Text.Length < 80)
            && System.Windows.Automation.AutomationProperties.GetHelpText(eq).Contains("Arrow keys", StringComparison.Ordinal),
            "EQ hover describes the active band's effect briefly while full keyboard instructions remain accessible.");
        return root;
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
