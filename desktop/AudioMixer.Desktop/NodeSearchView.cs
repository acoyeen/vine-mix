using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AudioMixer.Desktop;

internal sealed class NodeSearchView : UserControl
{
    private GraphConfiguration _graph = new();
    private readonly Func<string, bool> _choose;
    private readonly TextBlock _empty;
    internal TextBox SearchBox { get; }
    internal ListBox Results { get; }
    internal Button GoButton { get; }
    internal Button CloseButton { get; }

    internal NodeSearchView(Func<string, bool> choose, Action close)
    {
        _choose = choose;
        FontSize = 13;
        UseLayoutRounding = SnapsToDevicePixels = true;
        SetResourceReference(FontFamilyProperty, "NodeFontFamily");
        SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        SetResourceReference(BackgroundProperty, "ConsolePanelBrush");
        AutomationProperties.SetName(this, "Find node");
        var root = new Grid { Margin = new Thickness(20) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new() { Height = height });
        Content = root;
        var title = new TextBlock { Text = "Find node", FontSize = 21, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 16) };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        root.Children.Add(title);
        SearchBox = new TextBox { MinHeight = 34, Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(SearchBox, "Search nodes");
        AutomationProperties.SetHelpText(SearchBox, "Search by node name, type, device or app. Use Up and Down to choose a result, then Enter.");
        SearchBox.TextChanged += (_, _) => RefreshResults();
        SearchBox.PreviewKeyDown += (_, e) => { if (TrySearchKey(e.Key, Keyboard.Modifiers)) e.Handled = true; };
        Grid.SetRow(SearchBox, 1); root.Children.Add(SearchBox);
        var listArea = new Grid();
        Results = new ListBox { BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Results.SetResourceReference(BackgroundProperty, "ConsolePanelBrush");
        Results.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        ScrollViewer.SetHorizontalScrollBarVisibility(Results, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(Results, "Matching nodes");
        Results.SelectionChanged += (_, _) => GoButton!.IsEnabled = Results.SelectedItem is NodeSearchResult;
        Results.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { TryChooseSelected(); e.Handled = true; } };
        Results.MouseDoubleClick += (_, e) => {
            if (e.ChangedButton == MouseButton.Left
                && ItemsControl.ContainerFromElement(Results, e.OriginalSource as DependencyObject) is ListBoxItem item
                && item.Content is NodeSearchResult) {
                // ListBox owns capture through the second mouse-down. Release
                // only its own selection capture before invoking navigation.
                if (ReferenceEquals(Mouse.Captured, Results)) Results.ReleaseMouseCapture();
                TryChooseSelected(); e.Handled = true;
            }
        };
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(MarginProperty, new Thickness(8, 6, 8, 6));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(NodeSearchResult.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        stack.AppendChild(name);
        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(NodeSearchResult.Detail)));
        detail.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        detail.SetValue(MarginProperty, new Thickness(0, 3, 0, 0));
        detail.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        stack.AppendChild(detail);
        Results.ItemTemplate = new DataTemplate { VisualTree = stack };
        var resultStyle = new Style(typeof(ListBoxItem));
        resultStyle.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("ConsoleTextBrush")));
        resultStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        resultStyle.Setters.Add(new Setter(BackgroundProperty, System.Windows.Media.Brushes.Transparent));
        resultStyle.Setters.Add(new Setter(BorderBrushProperty, System.Windows.Media.Brushes.Transparent));
        resultStyle.Setters.Add(new Setter(BorderThicknessProperty, new Thickness(1)));
        var face = new FrameworkElementFactory(typeof(Border));
        face.SetBinding(Border.BackgroundProperty, new Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
        face.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
        face.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
        face.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
        face.AppendChild(presenter);
        resultStyle.Setters.Add(new Setter(TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = face }));
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ConsoleButtonFaceBrush")));
        resultStyle.Triggers.Add(hover);
        var chosen = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        chosen.Setters.Add(new Setter(BackgroundProperty, new DynamicResourceExtension("ConsoleButtonFaceBrush")));
        chosen.Setters.Add(new Setter(BorderBrushProperty, new DynamicResourceExtension("ConsoleABrush")));
        resultStyle.Triggers.Add(chosen);
        resultStyle.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(NodeSearchResult.Name))));
        resultStyle.Setters.Add(new Setter(AutomationProperties.HelpTextProperty, new Binding(nameof(NodeSearchResult.Detail))));
        Results.ItemContainerStyle = resultStyle;
        listArea.Children.Add(Results);
        _empty = new TextBlock { Margin = new Thickness(8, 16, 8, 0), TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        listArea.Children.Add(_empty);
        Grid.SetRow(listArea, 2); root.Children.Add(listArea);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        CloseButton = new Button { Content = "Close", MinWidth = 74, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4) };
        AutomationProperties.SetName(CloseButton, "Close node search");
        CloseButton.Click += (_, _) => close();
        GoButton = new Button { Content = "Go to node", MinWidth = 100, MinHeight = 32, Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(8, 0, 0, 0), IsEnabled = false };
        GoButton.Click += (_, _) => TryChooseSelected();
        buttons.Children.Add(CloseButton); buttons.Children.Add(GoButton);
        Grid.SetRow(buttons, 3); root.Children.Add(buttons);
    }

    internal void Apply(GraphConfiguration graph) { _graph = graph; RefreshResults(); }
    private void RefreshResults()
    {
        string? selectedId = (Results.SelectedItem as NodeSearchResult)?.Id;
        var matches = NodeSearch.Match(_graph, SearchBox.Text);
        _empty.Text = _graph.Nodes.Count == 0 ? "No nodes in this workspace." : "No matching nodes.";
        _empty.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // Meter snapshots arrive while search is open. Preserve list containers,
        // keyboard focus and scroll unless the searchable results really change.
        if (Results.ItemsSource is IReadOnlyList<NodeSearchResult> previous && previous.SequenceEqual(matches)) return;
        Results.ItemsSource = matches;
        int selected = -1;
        for (int i = 0; i < matches.Count; i++) if (matches[i].Id == selectedId) { selected = i; break; }
        Results.SelectedIndex = selected >= 0 ? selected : matches.Count > 0 ? 0 : -1;
        GoButton.IsEnabled = Results.SelectedItem is NodeSearchResult;
    }
    internal bool TrySearchKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None) return false;
        if (key == Key.Enter) { TryChooseSelected(); return true; }
        if (key is not (Key.Up or Key.Down)) return false;
        if (Results.Items.Count > 0) {
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1);
            Results.ScrollIntoView(Results.SelectedItem);
        }
        return true;
    }
    internal bool TryChooseSelected()
    {
        if (Results.SelectedItem is not NodeSearchResult result) return false;
        if (_choose(result.Id)) return true;
        RefreshResults(); return false;
    }
}
