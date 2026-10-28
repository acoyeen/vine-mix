using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>
/// Organizational canvas frame with an interactive background. Bounds and all
/// graph edits belong to the workspace; the frame has no routing behavior.
/// </summary>
internal sealed class GraphFrameView : Grid
{
    internal const double Padding = 24;
    private readonly Border _outline = new() { IsHitTestVisible = false };
    private readonly Border _header = new() { VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent, Focusable = true };
    private readonly Border _headerTint = new() { IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _title = new()
    {
        FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis,
        TextAlignment = TextAlignment.Center,
        IsHitTestVisible = false
    };
    private readonly TextBox _rename = new()
    {
        Visibility = Visibility.Collapsed, MaxLength = 80,
        VerticalContentAlignment = VerticalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        FontWeight = FontWeights.SemiBold
    };
    private Action<string>? _commitRename;
    private double _zoom = 1;
    private (string Name, string Color, int Count, double Zoom, bool Selected, bool Locked,
        bool HighContrast, Brush SelectedBrush, Brush TextBrush)? _appearance;

    internal FrameworkElement HeaderElement => _header;
    internal double EffectiveTitleSize => _title.FontSize * _zoom;
    internal double TitleScreenFontSize => EffectiveTitleSize;
    internal double EffectiveHeaderHeight => _header.Height * _zoom;
    internal string DisplayedName => _title.Text;
    internal Brush FrameBorderBrush => _outline.BorderBrush;
    internal bool IsRenaming => _rename.Visibility == Visibility.Visible;
    internal static double HeaderHeight(double zoom) => Math.Max(52, 32 / SafeZoom(zoom));
    internal static double MinimumWidth(double zoom) => Math.Max(260, 180 / SafeZoom(zoom));

    internal GraphFrameView()
    {
        // The exposed background selects/moves the group. Nodes and cables sit
        // above this view and continue to receive their own pointer events.
        Background = Brushes.Transparent;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);

        var headerContent = new Grid();
        headerContent.Children.Add(_title);
        headerContent.Children.Add(_rename);
        _header.Child = headerContent;
        Children.Add(_outline); Children.Add(_headerTint); Children.Add(_header);
        AutomationProperties.SetName(this, "Organizational group");
        AutomationProperties.SetName(_rename, "Group name");
        KeyboardNavigation.SetIsTabStop(_header, false);
        _rename.LostKeyboardFocus += (_, _) => FinishRename(commit: true, focusHeader: false);
        _rename.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Escape)) return;
            FinishRename(commit: args.Key == Key.Enter, focusHeader: true);
            args.Handled = true;
        };
        // Text editing owns its pointer events; a caret click must not start a
        // group move or replace the textbox's own context menu.
        _rename.MouseDown += (_, args) => args.Handled = true;
        _rename.MouseUp += (_, args) => args.Handled = true;
    }

    internal void Update(string name, string colorKey, int memberCount, double zoom, bool selected, bool locked)
    {
        _zoom = SafeZoom(zoom);
        var color = GraphFramePalette.Get(colorKey);
        bool highContrast = ConsoleTheme.PreviewHighContrast ?? SystemParameters.HighContrast;
        if (locked && IsRenaming) CancelRename();
        var selectedBrush = ConsoleTheme.Get("NodeSelectedBrush");
        var textBrush = ConsoleTheme.Get("ConsoleTextBrush");
        var appearance = (name, color.Key, memberCount, _zoom, selected, locked, highContrast,
            selectedBrush, textBrush);
        if (_appearance == appearance) return;
        _appearance = appearance;
        double headerHeight = HeaderHeight(_zoom);
        double inset = Math.Max(18, 10 / _zoom);
        double radius = Math.Max(6, 3 / _zoom);
        Brush outline = highContrast
            ? selected ? SystemColors.HighlightBrush : SystemColors.WindowTextBrush
            : selected ? selectedBrush : Frozen(color.Accent, 190);

        _outline.CornerRadius = new CornerRadius(radius);
        _outline.BorderThickness = new Thickness((selected ? 1.5 : 1) / _zoom);
        _outline.BorderBrush = outline;
        _outline.Background = highContrast ? Brushes.Transparent : Frozen(color.Accent, 44);
        double borderWidth = (selected ? 1.5 : 1) / _zoom;
        _headerTint.Height = headerHeight - borderWidth;
        _headerTint.Margin = new Thickness(borderWidth, borderWidth, borderWidth, 0);
        _headerTint.CornerRadius = new CornerRadius(radius, radius, 0, 0);
        _headerTint.Background = highContrast ? SystemColors.WindowBrush : Frozen(color.Accent, 34);
        _header.Height = headerHeight;
        _header.Padding = new Thickness(inset, 0, inset, 0);
        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        _title.Text = name;
        _title.FontSize = Math.Max(24, 15 / _zoom);
        _title.Foreground = highContrast ? SystemColors.WindowTextBrush : textBrush;
        string count = memberCount == 1 ? "1 node" : $"{memberCount} nodes";
        _rename.FontSize = _title.FontSize;
        _rename.Height = headerHeight - 8 / _zoom;
        _rename.Padding = new Thickness(4 / _zoom, 0, 4 / _zoom, 0);

        string help = locked
            ? "Layout locked. Right-click for group actions."
            : "Drag the group background to move its nodes. Double-click the heading to rename.";
        ToolTip = NodeToolTip.Create(name, locked ? $"{count} · Layout locked" : count);
        AutomationProperties.SetName(_header, $"{name}, {count}");
        AutomationProperties.SetHelpText(_header, help);
        AutomationProperties.SetName(this, name);
        AutomationProperties.SetHelpText(this, help);
    }

    internal void BeginRename(Action<string> commit)
    {
        if (IsRenaming) { _rename.Focus(); return; }
        _commitRename = commit;
        _rename.Text = _title.Text;
        _rename.Visibility = Visibility.Visible;
        _rename.Focus();
        _rename.SelectAll();
    }

    internal void CancelRename() => FinishRename(commit: false, focusHeader: false);

    internal bool TryCommitPendingRename()
    {
        if (!IsRenaming) return true;
        if (!IsEnabled || !_rename.IsEnabled || _rename.IsReadOnly || _appearance?.Locked == true) return false;
        FinishRename(commit: true, focusHeader: false);
        return true;
    }

    private void FinishRename(bool commit, bool focusHeader)
    {
        if (!IsRenaming) return;
        var callback = _commitRename;
        _commitRename = null;
        string name = _rename.Text.Trim();
        _rename.Visibility = Visibility.Collapsed;
        if (focusHeader) _header.Focus();
        if (commit) callback?.Invoke(string.IsNullOrEmpty(name) ? "Group" : name);
    }

    private static double SafeZoom(double zoom) => double.IsFinite(zoom) ? Math.Clamp(zoom, .3, 2) : 1;
    private static Brush Frozen(Color color, byte alpha = 255)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
