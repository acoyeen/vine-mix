using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace VineMix.Desktop.Controls;

/// <summary>Shared resources; Windows high-contrast colours replace every operational surface.</summary>
public static class ConsoleTheme
{
    private static bool _initialized;
    private static bool? _previewHighContrast;
    public static event EventHandler? Changed;
    internal static bool? PreviewHighContrast => _previewHighContrast;
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/" + typeof(ConsoleTheme).Assembly.GetName().Name + ";component/Theme.xaml", UriKind.Relative) });
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyPalette();
    }
    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))
            Application.Current.Dispatcher.InvokeAsync(ApplyPalette);
    }
    private static void ApplyPalette()
    {
        var palette = new Dictionary<string, Brush>
        {
            ["BrandActionBrush"] = Color("#2D6A4F"),
            ["BrandActionTextBrush"] = Color("#F5F8F6"), ["BrandHoverBrush"] = Color("#347B5B"),
            ["ConsoleWindowBrush"] = Color("#0C0D0F"), ["ConsolePanelBrush"] = Color("#15181E"),
            ["ConsoleTextBrush"] = Color("#E8EBF2"), ["ConsoleMutedBrush"] = Color("#A5ADBA"),
            ["ConsoleBorderBrush"] = Color("#303744"), ["ConsoleControlBorderBrush"] = Color("#343D49"),
            ["ConsoleWellBrush"] = Color("#080A0C"),
            ["ConsoleButtonFaceBrush"] = Color("#242A32"), ["ConsoleButtonHoverBrush"] = Color("#303B40"),
            ["ConsoleEdgeLightBrush"] = Color("#444B54"), ["ConsoleEdgeDarkBrush"] = Color("#101215"),
            ["ConsoleHeaderBandBrush"] = Color("#171A1E"), ["ConsoleFaderInactiveBrush"] = Color("#939BA5"),
            ["ConsoleTrackBrush"] = Color("#080A0C"),
            ["ConsoleABrush"] = Color("#78BE98"), ["ConsoleBBrush"] = Color("#78BE98"),
            ["ConsoleMuteBrush"] = Color("#D57370"), ["ConsoleAlertTextBrush"] = Color("#F1AAA4"),
            ["ConsoleFocusBrush"] = Color("#DCE5EF"),
            ["ConsoleInkBrush"] = Color("#08110D"), ["ConsoleMeterWellBrush"] = Color("#050607"),
            ["ConsoleMeterUnlitBrush"] = Color("#20262B"), ["ConsoleMeterLowBrush"] = Color("#65B88B"),
            ["ConsoleMeterMidBrush"] = Color("#E6BB5A"), ["ConsoleMeterHighBrush"] = Color("#E56F70"),
            ["NodeCanvasBrush"] = Color("#000000"), ["NodeGridBrush"] = Color("#1B1F23"),
            ["NodeOutlineBrush"] = Color("#30363D"), ["NodeSelectedBrush"] = Color("#DCE5EF"),
            ["NodeAudioBrush"] = Color("#78BE98"), ["NodeControlBrush"] = Color("#A0A8B3"), ["NodeSidechainBrush"] = Color("#B8A0D8"),
            ["NodeInputHeaderBrush"] = Color("#583747"), ["NodeOutputHeaderBrush"] = Color("#4A2D34"),
            ["NodeProcessorHeaderBrush"] = Color("#40365B"), ["NodeUtilityHeaderBrush"] = Color("#294650"),
            ["NodeControllerHeaderBrush"] = Color("#374D3C")
        };
        if (_previewHighContrast ?? SystemParameters.HighContrast)
        {
            foreach (string key in new[] { "ConsoleWindowBrush", "ConsolePanelBrush", "ConsoleWellBrush", "ConsoleMeterWellBrush", "ConsoleMeterUnlitBrush", "ConsoleButtonFaceBrush", "ConsoleButtonHoverBrush", "ConsoleHeaderBandBrush", "ConsoleTrackBrush" }) palette[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "ConsoleTextBrush", "ConsoleMutedBrush", "ConsoleBorderBrush", "ConsoleControlBorderBrush", "ConsoleAlertTextBrush", "ConsoleFocusBrush", "ConsoleEdgeLightBrush", "ConsoleEdgeDarkBrush" }) palette[key] = SystemColors.WindowTextBrush;
            foreach (string key in new[] { "ConsoleABrush", "ConsoleBBrush", "ConsoleMuteBrush", "ConsoleMeterLowBrush", "ConsoleMeterMidBrush", "ConsoleMeterHighBrush", "ConsoleFaderInactiveBrush" }) palette[key] = SystemColors.HighlightBrush;
            palette["ConsoleInkBrush"] = SystemColors.HighlightTextBrush;
            foreach (string key in new[] { "NodeCanvasBrush", "NodeInputHeaderBrush", "NodeOutputHeaderBrush", "NodeProcessorHeaderBrush", "NodeUtilityHeaderBrush", "NodeControllerHeaderBrush" }) palette[key] = SystemColors.WindowBrush;
            foreach (string key in new[] { "NodeOutlineBrush", "NodeAudioBrush", "NodeControlBrush", "NodeSidechainBrush" }) palette[key] = SystemColors.WindowTextBrush;
            palette["NodeSelectedBrush"] = SystemColors.HighlightBrush;
            palette["NodeGridBrush"] = SystemColors.GrayTextBrush;
            palette["BrandActionBrush"] = palette["BrandHoverBrush"] = SystemColors.HighlightBrush;
            palette["BrandActionTextBrush"] = SystemColors.HighlightTextBrush;
        }
        foreach (var pair in palette) Application.Current.Resources[pair.Key] = pair.Value;
        Changed?.Invoke(null, EventArgs.Empty);
    }
    /// <summary>Inert design verification only; changes this process's resources, never Windows settings.</summary>
    internal static void SetPreviewHighContrast(bool? enabled) { _previewHighContrast = enabled; ApplyPalette(); }
    public static Brush Get(string key) => Application.Current?.TryFindResource(key) as Brush ?? SystemColors.WindowTextBrush;
    public static Brush Color(string hex) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
}
