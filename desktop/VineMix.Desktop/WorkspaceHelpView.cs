using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Reference and isolated practice lessons. The host owns focus restoration.</summary>
internal sealed class WorkspaceHelpView : UserControl
{
    private sealed record Topic(string Id, string Title, string Description, Func<IHelpLesson>? Create);
    private readonly Topic[] _topics =
    [
        new("controls", "Controls", "Open a section, then choose a control for details.", null),
        new("dials", "Dials & values", "Drag vertically, or click a value to type.", HelpControlLessons.Dials),
        new("buttons", "Buttons & menus", "States, selectors and menu actions.", HelpControlLessons.Buttons),
        new("search", "Search & results", "Find a sample node by name, type, device or app.", HelpControlLessons.Search),
        new("equalizer", "Equalizer", "Select a band to edit its frequency, gain and width.", HelpAdvancedLessons.Equalizer),
        new("feedback", "Meters & status", "Levels, overloads and device or processing states.", HelpControlLessons.Feedback)
    ];
    private readonly Grid _body = new();
    private readonly ListBox _navigation = new();
    private readonly ComboBox _compactNavigation = new();
    private readonly StackPanel _page = new();
    private readonly StackPanel _content = new();
    private readonly TextBlock _pageTitle = HelpLessonUi.Text("", 15, true);
    private readonly TextBlock _description = HelpLessonUi.Hint("");
    private readonly Button _reset;
    private sealed record ReferenceSection(Button Header, System.Windows.Shapes.Path Arrow, Border Body);
    private sealed record ReferenceRow(Border Surface, Button Action, TextBlock Description);
    private readonly Dictionary<string, ReferenceSection> _referenceSections = [];
    private readonly Dictionary<string, ReferenceRow> _referenceRows = [];
    private string _selectedReferenceSection = "view";
    private bool _selecting;
    private Topic _selectedTopic;
    internal IHelpLesson? CurrentLesson { get; private set; }
    internal string CurrentTopic => _selectedTopic.Id;
    internal IReadOnlyList<string> TopicIds => _topics.Select(topic => topic.Id).ToArray();
    internal Button CloseButton { get; }
    internal Button ResetButton => _reset;
    internal ScrollViewer ScrollViewer { get; }
    internal ListBox TopicNavigation => _navigation;
    internal string SelectedReferenceSection => _selectedReferenceSection;
    internal string? HighlightedReferenceEntry { get; private set; }

    internal WorkspaceHelpView(Action close)
    {
        ArgumentNullException.ThrowIfNull(close);
        _selectedTopic = _topics[0];
        DataContext = new object();
        FontSize = 13;
        UseLayoutRounding = SnapsToDevicePixels = true;
        SetResourceReference(FontFamilyProperty, "NodeFontFamily");
        SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        SetResourceReference(BackgroundProperty, "ConsolePanelBrush");
        AutomationProperties.SetName(this, "Help & shortcuts");
        AutomationProperties.SetHeadingLevel(_pageTitle, AutomationHeadingLevel.Level2);
        var root = new Grid();
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Content = root;
        var header = new DockPanel { Margin = new Thickness(14, 8, 12, 8) };
        CloseButton = HelpLessonUi.Button("Close", close, "Close help and return to the workspace");
        CloseButton.Margin = new Thickness(16, 0, 0, 0);
        CloseButton.VerticalAlignment = VerticalAlignment.Top;
        KeyboardNavigation.SetTabIndex(CloseButton, 0);
        DockPanel.SetDock(CloseButton, Dock.Right); header.Children.Add(CloseButton);
        var title = HelpLessonUi.Text("Help & shortcuts", 16, true);
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        title.Margin = new Thickness(0); title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        var headerRule = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerRule.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush"); root.Children.Add(headerRule);
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(168) });
        _body.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_body, 1); root.Children.Add(_body);
        _navigation.ItemsSource = _topics;
        _navigation.DisplayMemberPath = nameof(Topic.Title);
        _navigation.BorderThickness = new Thickness(0, 0, 1, 0);
        _navigation.SetResourceReference(Control.BorderBrushProperty, "ConsoleBorderBrush");
        _navigation.Padding = new Thickness(4, 8, 6, 8);
        _navigation.Margin = new Thickness(0);
        _navigation.SetResourceReference(BackgroundProperty, "ConsolePanelBrush");
        _navigation.SetResourceReference(ForegroundProperty, "ConsoleTextBrush");
        _navigation.ItemContainerStyle = NavigationStyle();
        AutomationProperties.SetName(_navigation, "Help topics");
        KeyboardNavigation.SetTabIndex(_navigation, 1);
        _navigation.SelectionChanged += (_, _) => { if (!_selecting && _navigation.SelectedItem is Topic topic) SelectTopic(topic.Id); };
        _body.Children.Add(_navigation);
        var reading = new Grid { Margin = new Thickness(14, 12, 14, 0) };
        reading.RowDefinitions.Add(new() { Height = GridLength.Auto });
        reading.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        _compactNavigation.ItemsSource = _topics;
        _compactNavigation.DisplayMemberPath = nameof(Topic.Title);
        _compactNavigation.MinHeight = 28;
        _compactNavigation.Margin = new Thickness(0, 0, 0, 12);
        _compactNavigation.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(_compactNavigation, "Choose help topic");
        _compactNavigation.SelectionChanged += (_, _) => { if (!_selecting && _compactNavigation.SelectedItem is Topic topic) SelectTopic(topic.Id); };
        reading.Children.Add(_compactNavigation);
        _page.Children.Add(_pageTitle); _page.Children.Add(_description); _page.Children.Add(_content);
        ScrollViewer = new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false,
            PanningMode = PanningMode.VerticalOnly, Focusable = true, BorderThickness = new Thickness(0) };
        AutomationProperties.SetName(ScrollViewer, "Help topic and practice examples");
        AutomationProperties.SetHelpText(ScrollViewer, "Scroll to read. Choose a topic to practise a control. All examples are separate from your mix.");
        Grid.SetRow(ScrollViewer, 1); reading.Children.Add(ScrollViewer);
        Grid.SetColumn(reading, 1); _body.Children.Add(reading);
        var footer = new DockPanel { Margin = new Thickness(12, 6, 12, 6) };
        _reset = HelpLessonUi.Button("Reset example", ResetExample, "Restore this practice example to its starting state");
        _reset.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(_reset, Dock.Left); footer.Children.Add(_reset);
        var note = HelpLessonUi.Hint("Examples only. Your mix is unchanged.");
        note.VerticalAlignment = VerticalAlignment.Center; note.Margin = new Thickness(0); footer.Children.Add(note);
        var footerRule = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerRule.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
        Grid.SetRow(footerRule, 2); root.Children.Add(footerRule);
        SizeChanged += (_, _) => UpdateNavigation();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CurrentLesson?.Suspend(); };
        PreviewMouseWheel += RouteSampleWheel;
        SelectTopic("controls");
    }

    private static Style NavigationStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 1)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ConsoleMutedBrush")));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2, 0, 0, 0));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(ListBoxItem)) { VisualTree = border }));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ConsoleTextBrush")));
        selected.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("ConsoleButtonFaceBrush")));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ConsoleABrush")));
        style.Triggers.Add(selected);
        var focus = new Trigger { Property = IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ConsoleFocusBrush")));
        style.Triggers.Add(focus);
        return style;
    }

    private void UpdateNavigation()
    {
        bool compact = ActualWidth < 940;
        _navigation.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _compactNavigation.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        _body.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 168);
    }

    internal void SelectTopic(string id)
    {
        var topic = _topics.FirstOrDefault(item => item.Id == id) ?? throw new ArgumentOutOfRangeException(nameof(id));
        CurrentLesson?.Suspend(); CurrentLesson?.Dispose(); CurrentLesson = null;
        _content.Children.Clear(); _selectedTopic = topic;
        _referenceSections.Clear(); _referenceRows.Clear(); HighlightedReferenceEntry = null;
        _selecting = true;
        try { _navigation.SelectedItem = _compactNavigation.SelectedItem = topic; }
        finally { _selecting = false; }
        _pageTitle.Text = topic.Title; _description.Text = topic.Description;
        _reset.Visibility = topic.Create is null ? Visibility.Collapsed : Visibility.Visible;
        if (topic.Create is null) BuildReference(_content);
        else { CurrentLesson = topic.Create(); _content.Children.Add(CurrentLesson.View); }
        ScrollViewer.ScrollToTop();
    }

    internal void BeginSession() => SelectTopic(_selectedTopic.Id);
    internal void EndSession()
    {
        _compactNavigation.IsDropDownOpen = false;
        CurrentLesson?.Suspend(); CurrentLesson?.Dispose(); CurrentLesson = null;
        _content.Children.Clear();
        _referenceSections.Clear(); _referenceRows.Clear(); HighlightedReferenceEntry = null;
    }
    internal void ResetExample() { CurrentLesson?.Suspend(); CurrentLesson?.Reset(); }
    internal bool TryCancelInteraction()
    {
        if (_compactNavigation.IsDropDownOpen) { _compactNavigation.IsDropDownOpen = false; return true; }
        return CurrentLesson?.TryCancel() == true;
    }

    private void RouteSampleWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || Ancestor<GraphWorkspace>(source) is not null) return;
        bool dial = Ancestor<RotaryValueControl>(source) is not null;
        bool closedSelector = Ancestor<ComboBox>(source) is { IsDropDownOpen: false };
        if (!dial && !closedSelector) return;
        ScrollViewer.ScrollToVerticalOffset(ScrollViewer.VerticalOffset - e.Delta / 120d * 48);
        e.Handled = true;
    }

    internal static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }
        return null;
    }

    internal void ShowEntry(string entryId)
    {
        var entry = HelpReference.Entries.FirstOrDefault(item => item.Id == entryId);
        if (entry is null) return;
        SelectTopic("controls");
        SetReferenceSection(entry.SectionId);
        SelectReferenceEntry(entry.Id, scroll: true);
    }

    private static readonly ControlTemplate ReferenceButtonTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="Face" Background="{TemplateBinding Background}" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource ConsoleButtonHoverBrush}"/></Trigger>
            <Trigger Property="IsPressed" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource ConsoleControlBorderBrush}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);

    private void BuildReference(Panel parent)
    {
        foreach (var section in HelpReference.Entries.GroupBy(entry => entry.SectionId))
        {
            string sectionId = section.Key;
            string title = section.First().Section;
            var arrow = new System.Windows.Shapes.Path
            {
                Width = 10, Height = 10, StrokeThickness = 1.6,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(16, 0, 2, 0), IsHitTestVisible = false
            };
            arrow.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "ConsoleTextBrush");
            var label = HelpLessonUi.Text(title, 13, true);
            label.Margin = new Thickness(0); label.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetHeadingLevel(label, AutomationHeadingLevel.Level3);
            var heading = new DockPanel();
            DockPanel.SetDock(arrow, Dock.Right); heading.Children.Add(arrow); heading.Children.Add(label);
            var header = HelpLessonUi.Button(title, () =>
                SetReferenceSection(_selectedReferenceSection == sectionId ? "" : sectionId));
            header.ToolTip = null;
            header.Content = heading;
            header.Template = ReferenceButtonTemplate;
            header.HorizontalAlignment = HorizontalAlignment.Stretch;
            header.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            header.Padding = new Thickness(10, 9, 10, 9);
            header.Margin = new Thickness(0);
            var rule = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
            rule.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
            var body = new StackPanel();
            foreach (var entry in section) body.Children.Add(CreateReferenceRow(entry));
            var indented = new Border
            {
                Child = body, BorderThickness = new Thickness(1, 0, 0, 0),
                Margin = new Thickness(14, 5, 0, 4), Padding = new Thickness(12, 0, 0, 0)
            };
            indented.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
            var group = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            group.Children.Add(rule); group.Children.Add(indented); parent.Children.Add(group);
            _referenceSections.Add(sectionId, new(header, arrow, indented));
        }
        if (!_referenceSections.ContainsKey(_selectedReferenceSection)) _selectedReferenceSection = "view";
        SetReferenceSection(_selectedReferenceSection);
    }

    private Border CreateReferenceRow(HelpReferenceEntry entry)
    {
        var line = new Grid();
        line.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = new GridLength(210) });
        var title = HelpLessonUi.Text(entry.Title);
        title.Margin = new Thickness(0, 0, 16, 0);
        title.VerticalAlignment = VerticalAlignment.Center;
        var keys = HelpLessonUi.Text(entry.Keys, 12, true);
        keys.Margin = new Thickness(0); keys.VerticalAlignment = VerticalAlignment.Center;
        keys.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
        line.Children.Add(title); Grid.SetColumn(keys, 1); line.Children.Add(keys);
        var description = HelpLessonUi.Hint(entry.Description);
        description.Margin = new Thickness(0, 8, 0, 0);
        description.Visibility = Visibility.Collapsed;
        var content = new StackPanel(); content.Children.Add(line); content.Children.Add(description);
        var action = HelpLessonUi.Button(entry.Title, () => SelectReferenceEntry(entry.Id, scroll: false));
        action.Content = content; action.Template = ReferenceButtonTemplate;
        action.HorizontalAlignment = HorizontalAlignment.Stretch;
        action.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        action.Padding = new Thickness(8, 8, 6, 8); action.Margin = new Thickness(0);
        action.ToolTip = NodeToolTip.Create(entry.Title, entry.Keys, entry.Description);
        AutomationProperties.SetHelpText(action, entry.Keys + ". " + entry.Description + " Choose to show details.");
        var surface = new Border { Child = action, BorderThickness = new Thickness(0, 0, 0, 1) };
        surface.SetResourceReference(Border.BorderBrushProperty, "ConsoleBorderBrush");
        _referenceRows.Add(entry.Id, new(surface, action, description));
        return surface;
    }

    private void SetReferenceSection(string sectionId)
    {
        _selectedReferenceSection = sectionId;
        foreach (var item in _referenceSections)
        {
            bool expanded = item.Key == sectionId;
            item.Value.Arrow.Data = Geometry.Parse(expanded ? "M 1,3 L 5,7 L 9,3" : "M 3,1 L 7,5 L 3,9");
            item.Value.Header.SetResourceReference(Control.BackgroundProperty,
                expanded ? "ConsoleButtonHoverBrush" : "ConsoleButtonFaceBrush");
            item.Value.Body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetItemStatus(item.Value.Header, expanded ? "Expanded" : "Collapsed");
            AutomationProperties.SetHelpText(item.Value.Header,
                expanded ? "Choose to collapse this section." : "Choose to expand this section and close the others.");
        }
        if (HighlightedReferenceEntry is { } highlighted
            && HelpReference.Entries.First(item => item.Id == highlighted).SectionId != sectionId)
            ClearReferenceHighlight();
    }

    private void SelectReferenceEntry(string entryId, bool scroll)
    {
        if (!_referenceRows.ContainsKey(entryId)) return;
        ClearReferenceHighlight();
        HighlightedReferenceEntry = entryId;
        var row = _referenceRows[entryId];
        row.Action.SetResourceReference(Control.BackgroundProperty, "ConsoleButtonFaceBrush");
        row.Description.Visibility = Visibility.Visible;
        AutomationProperties.SetItemStatus(row.Action, "Details shown");
        if (!scroll) return;
        ScrollReferenceEntry(entryId);
        // Opening from a Settings popup can happen before the newly visible
        // Help surface has been arranged. Scroll after layout without focus.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (CurrentTopic == "controls" && HighlightedReferenceEntry == entryId) ScrollReferenceEntry(entryId);
        }));
    }

    private void ClearReferenceHighlight()
    {
        foreach (var row in _referenceRows.Values)
        {
            row.Action.Background = Brushes.Transparent;
            row.Description.Visibility = Visibility.Collapsed;
            AutomationProperties.SetItemStatus(row.Action, "Details hidden");
        }
        HighlightedReferenceEntry = null;
    }

    private void ScrollReferenceEntry(string entryId)
    {
        if (!_referenceRows.TryGetValue(entryId, out var row)) return;
        _page.UpdateLayout();
        double top = row.Surface.TransformToAncestor(_page).Transform(new Point()).Y;
        ScrollViewer.ScrollToVerticalOffset(Math.Max(0, top - 8));
    }
}
