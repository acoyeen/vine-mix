using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;

namespace VineMix.Desktop.Controls;

/// <summary>The monochrome two-piece VM identity; vector geometry stays crisp at native DPI.</summary>
public sealed class BrandMark : FrameworkElement
{
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(
        nameof(Ink), typeof(Brush), typeof(BrandMark),
        new FrameworkPropertyMetadata(Brushes.WhiteSmoke, FrameworkPropertyMetadataOptions.AffectsRender));
    internal static Geometry UpperGeometry { get; } = Shape("M2,0 H21 Q22,0 23,1 L50,28 L77,1 Q78,0 79,0 H98 Q101,0 99,3 L52,50 Q50,52 48,50 L1,3 Q-1,0 2,0 Z");
    internal static Geometry LowerGeometry { get; } = Shape("M3,24 Q3,21 5,23 L50,68 L95,23 Q97,21 97,24 V99 Q97,101 95,101 H81 Q79,101 79,99 V60 L52,87 Q50,89 48,87 L21,60 V99 Q21,101 19,101 H5 Q3,101 3,99 Z");
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    public BrandMark()
    {
        Width = 24; Height = 25; Focusable = false;
        SnapsToDevicePixels = UseLayoutRounding = true;
        SetResourceReference(InkProperty, "ConsoleTextBrush");
        AutomationProperties.SetName(this, "VineMix");
    }

    private static Geometry Shape(string data)
    {
        var geometry = Geometry.Parse(data); geometry.Freeze(); return geometry;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        double scale = Math.Min(ActualWidth / 100, ActualHeight / 102);
        if (!double.IsFinite(scale) || scale <= 0) return;
        drawing.PushTransform(new TranslateTransform((ActualWidth - 100 * scale) / 2, (ActualHeight - 102 * scale) / 2));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        drawing.DrawGeometry(Ink, null, UpperGeometry);
        drawing.DrawGeometry(Ink, null, LowerGeometry);
        drawing.Pop(); drawing.Pop();
    }
}
