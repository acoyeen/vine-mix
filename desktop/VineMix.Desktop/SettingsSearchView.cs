using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace VineMix.Desktop;

/// <summary>Searches existing menu commands and shared help without executing an action.</summary>
internal sealed class SettingsSearchView : StackPanel
{
    private static readonly DependencyProperty SearchViewProperty = DependencyProperty.RegisterAttached(
        "SearchView", typeof(SettingsSearchView), typeof(SettingsSearchView), new PropertyMetadata(null));
    private readonly ContextMenu _menu;
    private readonly Dictionary<UIElement, Visibility> _originalVisibility = [];
    private readonly MenuItem[] _roots;
    private readonly Separator _resultSeparator = new() { Visibility = Visibility.Collapsed };
    private readonly MenuItem _resultHost;
    private readonly TextBlock _placeholder, _empty, _resultHeading;
    private Action<string>? _showEntry;
    internal TextBox SearchBox { get; }
    internal ListBox Results { get; }
    internal IReadOnlyList<HelpReferenceEntry> Matches { get; private set; } = [];
    internal IReadOnlyList<MenuItem> MatchingSettings { get; private set; } = [];

    internal static SettingsSearchView Attach(ContextMenu menu, Action<string>? showEntry = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        if (Get(menu) is { } current)
        {
            if (showEntry is not null) current._showEntry = showEntry;
            return current;
        }
        var search = new SettingsSearchView(menu, showEntry);
        menu.SetValue(SearchViewProperty, search);
        return search;
    }

    internal static SettingsSearchView? Get(ContextMenu menu)
        => menu.GetValue(SearchViewProperty) as SettingsSearchView;

    private SettingsSearchView(ContextMenu menu, Action<string>? showEntry)
    {
        _menu = menu; _showEntry = showEntry;
        DataContext = new object();
        SetResourceReference(TextElement.FontFamilyProperty, "NodeFontFamily");
        _roots = menu.Items.OfType<MenuItem>().ToArray();
        foreach (UIElement element in menu.Items.OfType<UIElement>()) Remember(element);

        var field = new Grid { Margin = new Thickness(6, 6, 6, 5) };
        SearchBox = new TextBox { MinHeight = 30, Padding = new Thickness(8, 4, 8, 4) };
        AutomationProperties.SetName(SearchBox, "Search settings and help");
        AutomationProperties.SetHelpText(SearchBox,
            "Find a setting or instructions for a control. Try zoom, devices or dials. " +
            "Up and Down choose a help result; Enter opens its full instructions. " +
            "Tab moves to matching settings; Shift+Tab returns to search. Escape clears your search.");
        field.Children.Add(SearchBox);
        _placeholder = Text("Search settings and help", muted: true);
        _placeholder.IsHitTestVisible = false;
        _placeholder.VerticalAlignment = VerticalAlignment.Center;
        _placeholder.Margin = new Thickness(9, 0, 9, 0);
        field.Children.Add(_placeholder);
        Children.Add(field);

        var resultArea = new StackPanel { Margin = new Thickness(6, 3, 6, 4), DataContext = new object() };
        _resultHeading = Text("Controls & shortcuts", muted: true);
        _resultHeading.FontWeight = FontWeights.Medium;
        _resultHeading.Margin = new Thickness(6, 3, 6, 3);
        resultArea.Children.Add(_resultHeading);
        Results = new ListBox
        {
            BorderThickness = new Thickness(0), MaxHeight = 260, IsTabStop = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ItemTemplate = ResultTemplate(), ItemContainerStyle = ResultStyle()
        };
        Results.SetResourceReference(Control.BackgroundProperty, "ConsolePanelBrush");
        Results.SetResourceReference(Control.ForegroundProperty, "ConsoleTextBrush");
        ScrollViewer.SetHorizontalScrollBarVisibility(Results, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(Results, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(Results, "Matching control instructions");
        AutomationProperties.SetHelpText(Results,
            "Read these instructions here, or choose a result to open it in Help. Tab moves to matching settings.");
        resultArea.Children.Add(Results);
        _empty = Text("No matching settings or controls. Try zoom, devices or dials.", muted: true);
        _empty.Margin = new Thickness(6, 7, 6, 7);
        _empty.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_empty, AutomationLiveSetting.Polite);
        resultArea.Children.Add(_empty);
        _resultHost = Host(resultArea);
        _resultHost.Visibility = Visibility.Collapsed;

        menu.Items.Insert(0, Host(this));
        menu.Items.Insert(1, new Separator());
        menu.Items.Add(_resultSeparator);
        menu.Items.Add(_resultHost);
        SearchBox.TextChanged += (_, _) => Refresh();
        SearchBox.PreviewKeyDown += (_, args) =>
        {
            if (TrySearchKey(args.Key, Keyboard.Modifiers)) args.Handled = true;
        };
        Results.PreviewKeyDown += (_, args) =>
        {
            if (TrySearchKey(args.Key, Keyboard.Modifiers)) args.Handled = true;
        };
        menu.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (!args.Handled && TrySettingKey(args.Key, Keyboard.Modifiers, args.OriginalSource as DependencyObject))
                args.Handled = true;
        }), true);
        Results.MouseDoubleClick += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left
                && ItemsControl.ContainerFromElement(Results, args.OriginalSource as DependencyObject) is ListBoxItem)
            {
                if (ReferenceEquals(Mouse.Captured, Results)) Results.ReleaseMouseCapture();
                TryChooseSelected(); args.Handled = true;
            }
        };
        menu.Opened += (_, _) =>
        {
            // Reset before requesting focus. Detached construction never opens or focuses a popup.
            ResetQuery();
            if (menu.IsOpen && PresentationSource.FromVisual(menu) is not null) SearchBox.Focus();
        };
        menu.Closed += (_, _) => ResetQuery();
        Refresh();
    }

    internal void ResetQuery()
    {
        if (SearchBox.Text.Length == 0) Refresh();
        else SearchBox.Clear();
    }

    internal bool TrySearchKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.A && modifiers == ModifierKeys.Control && SearchBox.IsKeyboardFocusWithin)
        { SearchBox.SelectAll(); return true; }
        if (key == Key.Tab && modifiers == ModifierKeys.None) return TryFocusSetting();
        if (modifiers != ModifierKeys.None) return false;
        if (key == Key.Escape)
        {
            if (SearchBox.Text.Length == 0) return false;
            SearchBox.Clear();
            if (_menu.IsOpen && PresentationSource.FromVisual(_menu) is not null) SearchBox.Focus();
            return true;
        }
        if (key == Key.Enter) { TryChooseSelected(); return true; }
        if (key is not (Key.Up or Key.Down)) return false;
        if (Matches.Count > 0)
        {
            Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (key == Key.Down ? 1 : -1), 0, Matches.Count - 1);
            Results.ScrollIntoView(Results.SelectedItem);
        }
        else TryFocusSetting();
        return true;
    }

    internal bool TrySettingKey(Key key, ModifierKeys modifiers, DependencyObject? source)
    {
        if (key != Key.Tab || modifiers != ModifierKeys.Shift || !CanFocusPopup) return false;
        for (var current = source; current is not null; current = current is Visual
            ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is not MenuItem row) continue;
            return _originalVisibility.ContainsKey(row) && SearchBox.Focus();
        }
        return false;
    }

    private bool CanFocusPopup => _menu.IsOpen && PresentationSource.FromVisual(_menu) is not null;

    private bool TryFocusSetting()
        => CanFocusPopup && _roots.FirstOrDefault(row => row.IsEnabled && row.Visibility == Visibility.Visible)?.Focus() == true;

    internal bool TryChooseSelected()
    {
        if (Results.SelectedItem is not HelpReferenceEntry entry || _showEntry is null) return false;
        _menu.IsOpen = false;
        _showEntry(entry.Id);
        return true;
    }

    private void Remember(UIElement element)
    {
        _originalVisibility[element] = element.Visibility;
        if (element is MenuItem parent)
            foreach (UIElement child in parent.Items.OfType<UIElement>()) Remember(child);
    }

    private void Refresh()
    {
        string query = SearchBox.Text.Trim();
        bool searching = query.Length > 0;
        _placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_menu.IsOpen) MenuHoverHelp.Attach(_menu).RequestPointerHelp(null);
        string[] terms = Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (var original in _originalVisibility)
            original.Key.SetCurrentValue(UIElement.VisibilityProperty, original.Value);
        if (searching)
        {
            foreach (var root in _roots) Filter(root, terms);
            foreach (var separator in _originalVisibility.Keys.OfType<Separator>()) separator.Visibility = Visibility.Collapsed;
        }
        MatchingSettings = _originalVisibility.Keys.OfType<MenuItem>()
            .Where(row => row.Visibility == Visibility.Visible).ToArray();
        string? selected = (Results.SelectedItem as HelpReferenceEntry)?.Id;
        Matches = searching ? HelpReference.Search(query) : [];
        Results.ItemsSource = Matches;
        int previous = Matches.ToList().FindIndex(entry => entry.Id == selected);
        Results.SelectedIndex = previous >= 0 ? previous : Matches.Count > 0 ? 0 : -1;
        _resultHost.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        _resultSeparator.Visibility = searching && MatchingSettings.Count > 0 && Matches.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;
        _resultHeading.Visibility = Matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Results.Visibility = Matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = searching && MatchingSettings.Count == 0 && Matches.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        if (searching && Matches.Count == 0 && MatchingSettings.Count > 0) _resultHost.Visibility = Visibility.Collapsed;
    }

    private bool Filter(MenuItem row, string[] terms, bool parentMatches = false)
    {
        row.IsSubmenuOpen = false;
        string text = Normalize(string.Join(" ", row.Header, AutomationProperties.GetHelpText(row),
            ContentText(row.ToolTip), Aliases(row.Header as string)));
        bool ownMatch = parentMatches || terms.All(term => text.Contains(term, StringComparison.Ordinal));
        bool childrenMatch = false;
        foreach (var child in row.Items.OfType<MenuItem>()) childrenMatch |= Filter(child, terms, ownMatch);
        bool visible = _originalVisibility[row] == Visibility.Visible && (ownMatch || childrenMatch);
        row.SetCurrentValue(UIElement.VisibilityProperty, visible ? Visibility.Visible : Visibility.Collapsed);
        return visible;
    }

    private static string Aliases(string? header) => header switch
    {
        "Right-click drag to pan" => "canvas navigation mouse panning",
        "Reduce meter animation" => "motion accessibility meters",
        "Audio device visibility" => "filters hidden inputs outputs",
        "Start VineMix when I sign in" => "startup windows login launch automatic",
        "Windows sound settings" => "audio hardware system devices",
        "Retry assigned devices" => "reconnect refresh recovery devices",
        "Restart audio engine" => "restart service reconnect recovery",
        _ => ""
    };

    private static string ContentText(object? value) => value switch
    {
        string text => text,
        TextBlock text => text.Text,
        Panel panel => string.Join(" ", panel.Children.Cast<UIElement>().Select(ContentText)),
        ContentControl control => ContentText(control.Content),
        _ => ""
    };

    private static string Normalize(string value)
    {
        var text = new StringBuilder();
        foreach (char character in value.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                text.Append(char.ToLowerInvariant(character));
        return text.ToString();
    }

    private static MenuItem Host(UIElement header)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty,
            new Binding(nameof(MenuItem.Header)) { RelativeSource = RelativeSource.TemplatedParent });
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        return new MenuItem
        {
            Header = header, StaysOpenOnClick = true, Focusable = false, MinHeight = 0,
            Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Template = new ControlTemplate(typeof(MenuItem)) { VisualTree = presenter }
        };
    }

    private static TextBlock Text(string value, bool muted)
    {
        var text = new TextBlock { Text = value, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        text.SetResourceReference(TextBlock.ForegroundProperty, muted ? "ConsoleMutedBrush" : "ConsoleTextBrush");
        return text;
    }

    private static DataTemplate ResultTemplate() => (DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
          <StackPanel Margin="7,6,7,7">
            <DockPanel LastChildFill="True">
              <TextBlock DockPanel.Dock="Right" Text="{Binding Keys}" FontFamily="{DynamicResource NodeNumericFontFamily}" FontSize="11" Foreground="{DynamicResource ConsoleMutedBrush}" Margin="10,1,0,0" TextWrapping="NoWrap"/>
              <TextBlock Text="{Binding Title}" FontSize="13" FontWeight="Medium" TextWrapping="Wrap"/>
            </DockPanel>
            <TextBlock Text="{Binding Description}" FontSize="12" Foreground="{DynamicResource ConsoleMutedBrush}" Margin="0,2,0,0" TextWrapping="Wrap"/>
          </StackPanel>
        </DataTemplate>
        """);

    private static Style ResultStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(2, 0, 0, 0)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        style.Setters.Add(new Setter(AutomationProperties.NameProperty, new Binding(nameof(HelpReferenceEntry.Title))));
        style.Setters.Add(new Setter(AutomationProperties.AcceleratorKeyProperty, new Binding(nameof(HelpReferenceEntry.Keys))));
        style.Setters.Add(new Setter(AutomationProperties.HelpTextProperty, new Binding(nameof(HelpReferenceEntry.Description))));
        var face = new FrameworkElementFactory(typeof(Border));
        foreach (var property in new[] { Border.BackgroundProperty, Border.BorderBrushProperty, Border.BorderThicknessProperty })
            face.SetBinding(property, new Binding(property.Name) { RelativeSource = RelativeSource.TemplatedParent });
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, new Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
        content.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate") { RelativeSource = RelativeSource.TemplatedParent });
        face.AppendChild(content);
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = face }));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ConsoleButtonFaceBrush")));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ConsoleMutedBrush")));
        style.Triggers.Add(selected);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ConsoleButtonHoverBrush")));
        style.Triggers.Add(hover);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ConsoleFocusBrush")));
        style.Triggers.Add(focus);
        return style;
    }
}
