using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Xml.Linq;

namespace VineMix.Desktop.Controls;

/// <summary>The approved wide identity, drawn from its packaged vector artwork.</summary>
public sealed class BrandWordmark : FrameworkElement
{
    public static readonly DependencyProperty AccentProperty = BrandMark.AccentProperty.AddOwner(typeof(BrandWordmark));
    public static readonly DependencyProperty InkProperty = BrandMark.InkProperty.AddOwner(typeof(BrandWordmark));
    private static readonly XDocument Artwork = ReadArtwork();
    internal static Geometry AccentGeometry { get; } = Build("vine", BrandMark.UpperGeometry);
    internal static Geometry InkGeometry { get; } = Build("mix", BrandMark.LowerGeometry);
    internal static Rect ArtworkBounds { get; } = Rect.Union(AccentGeometry.Bounds, InkGeometry.Bounds);

    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }

    public BrandWordmark()
    {
        Width = 106; Height = 20; Focusable = false;
        SnapsToDevicePixels = UseLayoutRounding = true;
        SetResourceReference(AccentProperty, "BrandAccentBrush");
        SetResourceReference(InkProperty, "ConsoleTextBrush");
        AutomationProperties.SetName(this, "VineMix");
    }

    private static XDocument ReadArtwork()
    {
        var resource = Application.GetResourceStream(new Uri("/" + typeof(BrandWordmark).Assembly.GetName().Name + ";component/Assets/Branding/wordmark.svg", UriKind.Relative))
            ?? throw new InvalidOperationException("The packaged VineMix wordmark is missing.");
        using var stream = resource.Stream;
        return XDocument.Load(stream);
    }

    private static Geometry Build(string groupId, Geometry symbolPart)
    {
        var result = new GeometryGroup();
        Geometry symbol = symbolPart.Clone();
        symbol.Transform = new MatrixTransform(2, 0, 0, 2, 578, 253);
        result.Children.Add(symbol);
        foreach (XElement element in Artwork.Descendants().Single(element => (string?)element.Attribute("id") == groupId).Descendants())
        {
            Geometry? geometry = element.Name.LocalName switch
            {
                "path" => Geometry.Parse((string)element.Attribute("d")!),
                "rect" => new RectangleGeometry(new Rect(Number(element, "x"), Number(element, "y"),
                    Number(element, "width"), Number(element, "height")), Number(element, "rx"), Number(element, "rx")),
                _ => null
            };
            if (geometry is not null) result.Children.Add(geometry);
        }
        result.Freeze();
        return result;
    }

    private static double Number(XElement element, string name) =>
        double.Parse((string)element.Attribute(name)!, CultureInfo.InvariantCulture);

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        Rect bounds = ArtworkBounds;
        double scale = Math.Min(ActualWidth / bounds.Width, ActualHeight / bounds.Height);
        if (!double.IsFinite(scale) || scale <= 0) return;
        // Fit painted bounds rather than the SVG preview canvas, keeping equal
        // breathing room above and below the complete symbol and wordmark.
        drawing.PushTransform(new TranslateTransform((ActualWidth - bounds.Width * scale) / 2 - bounds.X * scale,
            (ActualHeight - bounds.Height * scale) / 2 - bounds.Y * scale));
        drawing.PushTransform(new ScaleTransform(scale, scale));
        drawing.DrawGeometry(Accent, null, AccentGeometry);
        drawing.DrawGeometry(Ink, null, InkGeometry);
        drawing.Pop(); drawing.Pop();
    }
}
