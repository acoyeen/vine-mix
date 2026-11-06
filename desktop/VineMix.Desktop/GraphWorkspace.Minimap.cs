using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace VineMix.Desktop;

public sealed partial class GraphWorkspace
{
    private readonly GraphMinimap _minimap = new()
    {
        Width = 220, Height = 144, Margin = new Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom
    };
    private Point _minimapOriginalPan;
    internal GraphMinimap Minimap => _minimap;

    internal bool TryMinimapGestureKey(Key key)
    {
        if (!_minimap.IsDragging) return false;
        // The map owns this gesture even when keyboard focus remained in a
        // numeric editor. Keep its draft intact and prevent camera/graph commands
        // from changing the frozen projection before release.
        if (key == Key.Escape) { _minimap.EndPan(cancel: true); SetMessage("Pan cancelled."); }
        return true;
    }

    private void InitializeMinimap(Grid root)
    {
        // A sibling overlay cannot bubble into the canvas's node/socket gestures.
        Grid.SetRow(_minimap, 1); root.Children.Add(_minimap);
        _minimap.CanBeginNavigation = () => CanFrameSelection;
        _minimap.NavigationStarted += () => { _minimapOriginalPan = new(_panX, _panY); };
        _minimap.CenterRequested += center =>
        {
            _panX = _viewport.ActualWidth / 2 - center.X * _zoom;
            _panY = _viewport.ActualHeight / 2 - center.Y * _zoom;
            ApplyTransform();
        };
        _minimap.NavigationFinished += cancelled =>
        {
            if (cancelled) { _panX = _minimapOriginalPan.X; _panY = _minimapOriginalPan.Y; ApplyTransform(); }
            else PersistView();
            RefreshMinimap();
        };
        RefreshMinimap();
    }

    private void RefreshMinimap()
    {
        bool visible = _mixer.ShowMinimap && _viewport.ActualWidth >= 120 && _viewport.ActualHeight >= 100;
        if (!visible) _minimap.EndPan(cancel: true);
        _minimap.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        // Keep the map usable on smaller windows while limiting its footprint.
        _minimap.Width = Math.Min(220, Math.Max(96, _viewport.ActualWidth * .24));
        _minimap.Height = Math.Min(144, Math.Max(72, _viewport.ActualHeight * .24));
        var viewport = new Rect(-_panX / _zoom, -_panY / _zoom, _viewport.ActualWidth / _zoom, _viewport.ActualHeight / _zoom);
        _minimap.SetScene(_nodes.Select(pair =>
        {
            var view = pair.Value;
            double height = view.ActualHeight > 0 ? view.ActualHeight : view.DesiredSize.Height;
            return new MinimapNode(new Rect(Canvas.GetLeft(view), Canvas.GetTop(view), view.Width, Math.Max(1, height)), _selectedNodes.Contains(pair.Key));
        }), viewport);
    }
}
