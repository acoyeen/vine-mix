using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace AudioMixer.Desktop.Controls;

/// <summary>Informational hover readings around the unchanged meter.</summary>
internal sealed class MeterPeakDisplay : ContentControl
{
    private static readonly ControlTemplate MeterTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ContentControl">
          <Grid Background="{TemplateBinding Background}" SnapsToDevicePixels="True">
            <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Stretch" VerticalAlignment="Stretch"/>
            <Border x:Name="FocusEdge" BorderBrush="Transparent" BorderThickness="1" CornerRadius="2" IsHitTestVisible="False"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="FocusEdge" Property="BorderBrush" Value="{DynamicResource ConsoleFocusBrush}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private readonly TextBlock _firstReading;
    private readonly TextBlock _secondReading;
    private readonly TextBlock _clipReading;
    private string _accessibleDescription = "";

    internal StereoNodeMeter Meter { get; }
    internal StackPanel PeakToolTipContent { get; }
    internal string PeakReadingText { get; private set; } = "";
    internal string ClipReadingText { get; private set; } = "";

    public MeterPeakDisplay(StereoNodeMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        Meter = meter;
        Width = MinWidth = 28;
        MinHeight = 90;
        Height = double.NaN;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Margin = Padding = BorderThickness = new Thickness(0);
        Background = BorderBrush = Brushes.Transparent;
        Cursor = Cursors.Arrow;
        Focusable = IsTabStop = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = UseLayoutRounding = true;
        Template = MeterTemplate;
        meter.IsHitTestVisible = false;
        Content = meter;

        // The meter's existing tooltip can be a parented UIElement. Keep a
        // separate panel for this control and update its existing text blocks.
        PeakToolTipContent = NodeToolTip.Create("Highest observed peak", "No readings yet", "Right",
            "Over 0 dBFS detected", "Sample peaks");
        _firstReading = (TextBlock)PeakToolTipContent.Children[1];
        _secondReading = (TextBlock)PeakToolTipContent.Children[2];
        _clipReading = (TextBlock)PeakToolTipContent.Children[3];
        ToolTip = PeakToolTipContent;
        AutomationProperties.SetName(this, "Audio peaks");
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Off);
        meter.PeakReadingsChanged += (_, _) => RefreshReadings();
        RefreshReadings();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PeakDisplayAutomationPeer(this);

    private sealed class PeakDisplayAutomationPeer(MeterPeakDisplay owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(MeterPeakDisplay);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        public override object? GetPattern(PatternInterface patternInterface) => null;
    }

    private void RefreshReadings()
    {
        string first = !Meter.HasPeakReadings ? "No readings yet" :
            (Meter.Channels == 1 ? "Peak · " : "Left · ") + FormatPeak(Meter.RetainedPeakLeft);
        string second = Meter.HasPeakReadings && Meter.Channels != 1
            ? "Right · " + FormatPeak(Meter.RetainedPeakRight) : "";
        string clip = Meter.ClipLatched ? "Over 0 dBFS detected" : "";
        SetLine(_firstReading, first);
        SetLine(_secondReading, second);
        SetLine(_clipReading, clip);
        PeakReadingText = second.Length == 0 ? first : first + "; " + second;
        ClipReadingText = clip;

        string description = "Highest observed sample peaks. " + PeakReadingText + "." +
            (clip.Length == 0 ? "" : " " + clip + ".");
        // A higher unrounded sample need not change a displayed tenth of a dB.
        // Avoid redundant text/automation updates, and never raise live events.
        if (_accessibleDescription == description) return;
        _accessibleDescription = description;
        AutomationProperties.SetHelpText(this, description);
        AutomationProperties.SetItemStatus(this, description);
    }

    private static void SetLine(TextBlock line, string text)
    {
        if (line.Text != text) line.Text = text;
        Visibility visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (line.Visibility != visibility) line.Visibility = visibility;
    }

    private static string FormatPeak(double amplitude)
    {
        if (!double.IsFinite(amplitude) || amplitude <= 0) return "−∞ dBFS";
        double db = Math.Min(0, 20 * Math.Log10(Math.Min(amplitude, 1)));
        // Native telemetry is capped at full scale; the clip latch is separate
        // evidence and must never be converted into an invented positive peak.
        double rounded = Math.Round(db, 1, MidpointRounding.AwayFromZero);
        if (rounded == 0) return "0.0 dBFS";
        return rounded.ToString("0.0", CultureInfo.InvariantCulture).Replace('-', '−') + " dBFS";
    }
}
