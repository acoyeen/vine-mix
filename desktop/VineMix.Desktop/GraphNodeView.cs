using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal sealed class GraphNodeView : Border
{
    private readonly GraphWorkspace _owner;
    private readonly string _kind;
    private readonly Grid _root = new();
    private readonly StackPanel _body = new();
    private readonly Grid _content = new() { Margin = new Thickness(12, 7, 12, 10) };
    private readonly StackPanel _inputs = new() { Margin = new Thickness(0, 3, 0, 7) };
    private readonly NodeSelectionOutline _selectionOutline = new() { IsHitTestVisible = false, Visibility = Visibility.Hidden };
    private readonly AvailabilityWarningGlow _availabilityWarningGlow = new() { IsHitTestVisible = false, Visibility = Visibility.Hidden };
    private readonly System.Windows.Shapes.Path _availabilityWarningIcon = new()
    {
        Width = 18, Height = 16, Stretch = Stretch.Fill, Margin = new Thickness(17, 0, 0, 0),
        HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false, Visibility = Visibility.Hidden
    };
    private AudioAvailability? _availabilityNotice;
    private string _availabilityNoticePathMessage = "";
    private object? _availabilityNoticeHelp, _availabilityDefaultHelp;
    private readonly SolidColorBrush _availabilityWarningText = new();
    private AnimationClock? _availabilityPulseClock;
    private Color _availabilityPulseRed, _availabilityPulseNormal;
    private bool _availabilityPulseReduced, _availabilityTextWarned;
    private readonly Canvas _grabLayer = new();
    private readonly Border _grabTop = GrabArea(), _grabLeft = GrabArea(), _grabRight = GrabArea();
    private readonly List<(GraphWorkspace.Socket Socket, FrameworkElement Row)> _portAnchors = [];
    private readonly bool _gatePresentation, _legacyCompressorPresentation;
    private readonly Grid? _outputRow;
    private bool _socketLayoutDirty;
    private readonly TextBlock _title, _status;
    private readonly TextBox _rename;
    private readonly Button? _mute;
    private readonly List<GraphWorkspace.Socket> _sockets = [];
    private readonly ComboBox? _device, _app;
    private readonly TextBlock? _virtualDevice;
    private readonly Button? _mono;
    private readonly List<ComboBox> _dropDowns = [];
    private readonly RotaryValueControl? _gain;
    private readonly RotaryValueControl? _multiplier;
    private readonly TextBlock? _legacyMultiplierValue;
    private readonly bool _hasLevelInput, _legacyMultiplierPresentation;
    private EqualizerEditor? _eqPlot;
    private ReductionStrip? _reductionStrip;
    private ulong _dynamicsFrame;
    private float _dynamicsReduction, _dynamicsDetector = -96;
    private bool _dynamicsValid;
    private double _lastDynamicsTime = double.NegativeInfinity;
    private StackPanel? _dynamicsTip;
    private object? _savedLevelTip;
    internal double VisibleReductionDb { get; private set; }
    internal string DynamicsFeedbackText => _reductionStrip is null ? "" : _status.Text;
    internal object? DynamicsHelpContent => _dynamicsTip;
    private readonly NodeMeter _meter;
    private readonly MeterPeakDisplay _meterPeakDisplay;
    private long _peakSampleSequence = -1;
    private GraphNodeStatus? _peakSampleStatus;
    private (string Device, string Stable, int Channel, bool Mono, uint Process, string App) _peakSource;
    private bool _peakSourceKnown, _skipPeakSample, _awaitPeakFrameAdvance, _peakFrameKnown;
    private ulong _peakBoundaryFrame, _peakLastAudioFrame;
    private string _peakEndpointId = "";
    private Button? _effectToggle;
    private readonly List<Action<GraphNode>> _effectUpdates = [];
    private UniformGrid? _dialGroup;
    private double _dialGroupHeight = 58;
    private TextBlock? _detectorReadout;
    private GraphNode _node;
    private bool _applying;
    private IReadOnlyList<Endpoint>? _choiceEndpoints;
    private IReadOnlyList<AudioApplication>? _choiceApplications;
    private (string Id, string StableId, string Name, int Channel) _deviceBindingKey;
    private DeviceVisibility _deviceVisibility;
    private (uint ProcessId, string Path, string Name) _appBindingKey;
    internal int DeviceChoiceBuildCount { get; private set; }
    internal int AppChoiceBuildCount { get; private set; }
    private double _viewScale = 1;
    public string Id => _node.Id;
    internal double ViewScale => _viewScale;
    internal double EffectiveTitleSize => _title.FontSize * _viewScale;
    internal bool DetailsVisible => _body.Visibility == Visibility.Visible;
    internal string HeaderBrushKey => NodeCatalog.HeaderBrushKey(_kind);
    internal bool HasOpenDropDown => _dropDowns.Any(choice => choice.IsDropDownOpen);
    internal bool IsPointerOverOpenDropDown()
    {
        // ComboBox mouse capture can report the owner as hovered even outside
        // the menu. Compare actual pointer coordinates with the two surfaces.
        static bool ContainsPointer(FrameworkElement surface) => surface.IsVisible
            && new Rect(surface.RenderSize).Contains(Mouse.GetPosition(surface));
        return _dropDowns.Any(choice => choice.IsDropDownOpen && (ContainsPointer(choice)
            || choice.Template?.FindName("PART_Popup", choice) is Popup { Child: FrameworkElement child } && ContainsPointer(child)));
    }
    internal bool SelectionOutlineVisible => _selectionOutline.Visibility == Visibility.Visible;
    internal double SelectionOutlineScreenThickness => _selectionOutline.StrokeThickness * _viewScale;
    internal string AvailabilityNoticeLabel => _availabilityNotice?.Label ?? "";
    internal string AvailabilityNoticeDetail => _availabilityNotice?.Detail ?? "";
    internal bool AvailabilityNoticeVisible => false;
    internal bool AvailabilityWarningVisible => _availabilityWarningGlow.Visibility == Visibility.Visible;
    internal bool AvailabilityWarningIconVisible => _availabilityWarningIcon.Visibility == Visibility.Visible;
    internal System.Windows.Shapes.Path AvailabilityWarningIconElement => _availabilityWarningIcon;
    internal bool AvailabilityWarningPulseActive => _availabilityPulseClock is not null;
    internal object? AvailabilityWarningPulseIdentity => _availabilityPulseClock;
    internal const double AvailabilityWarningPulsePeriodSeconds = 1.6;
    internal double AvailabilityWarningScreenRadius => AvailabilityWarningGlow.ScreenRadius;
    internal FrameworkElement AvailabilityGlowElement => _availabilityWarningGlow;
    internal FrameworkElement? AvailabilityDeviceElement => _kind switch
    {
        "microphone" or "output" => _device, "appInput" => _app,
        "virtualInput" or "virtualOutput" => _virtualDevice, _ => null
    };
    internal TextBlock? AvailabilityDeviceTextElement => AvailabilityDeviceElement is TextBlock text ? text
        : AvailabilityDeviceElement is ComboBox combo ? SelectedSourceText(combo) : null;
    internal TextBlock AvailabilityNoticeDetailTextElement => _status;
    internal MeterPeakDisplay PeakMeterDisplay => _meterPeakDisplay;
    internal bool MatchesPresentation(GraphNode node) => node.Kind == _kind
        && (_kind is not ("expander" or "gate") || _gatePresentation == IsGate(node))
        && (_kind != "compressor" || _legacyCompressorPresentation == (node.DynamicsUiVersion == 0))
        && _hasLevelInput == LinkedLevelControl.ShowInput(_owner.Graph, node)
        && _legacyMultiplierPresentation == (node.Kind == "control" && LinkedLevelControl.HasLegacyValue(node));
    private static bool IsGate(GraphNode node) => node.Kind == "gate" || node.Kind == "expander" && node.Expander.Mode == "gate";
    internal double HeaderGrabTopPadding => 8;
    internal double HeaderGrabSidePadding => 6;
    internal Point GetHeaderGrabTestPoint() => new(ActualWidth / 2, -HeaderGrabTopPadding / _viewScale / 2);

    public GraphNodeView(GraphWorkspace owner, GraphNode node)
    {
        _owner = owner; _node = node; _kind = node.Kind;
        _hasLevelInput = LinkedLevelControl.ShowInput(owner.Graph, node);
        _legacyMultiplierPresentation = node.Kind == "control" && LinkedLevelControl.HasLegacyValue(node);
        _gatePresentation = IsGate(node); _legacyCompressorPresentation = node.DynamicsUiVersion == 0;
        Width = node.Kind switch { "eq" => 620, "compressor" or "expander" or "gate" => 428, "voiceColor" => 392, "tone" => 376, "gain" or "pan" or "mono" or "reroute" or "channelSwap" or "stereoWidth" or "stereoPan" or "stereoCombine" or "stereoSplit" => 316, "control" => 280, "mix" => 316, _ => 356 };
        BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(5); SnapsToDevicePixels = true; UseLayoutRounding = true;
        // Whole-node effects put text into an intermediate render target.
        // Keep the control tree directly rendered at the display's DPI.
        // GDI-compatible Display metrics round glyph advances before the canvas
        // transform, leaving uneven letter spacing at fractional zoom. Ideal
        // keeps vector font metrics until rendering at the final display scale.
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);
        // These controls are continuously scaled by the node canvas. RGB
        // subpixel antialiasing must not be minified into coloured glyph edges.
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        SetResourceReference(BackgroundProperty, "ConsolePanelBrush"); SetResourceReference(BorderBrushProperty, "NodeOutlineBrush");
        _root.RowDefinitions.Add(new() { Height = new GridLength(32) });
        _root.RowDefinitions.Add(new() { Height = new GridLength(node.Kind == "stereoSplit" ? 56 : GraphDefaults.HasAudioOutput(node.Kind) || node.Kind == "control" ? 28 : 0) });
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto }); _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new Grid { Cursor = Cursors.Arrow, ToolTip = NodeDescription(_gatePresentation ? "gate" : node.Kind) };
        if (node.Kind == "ducking")
        {
            var staticHelp = DuckingDescription("static"); var dynamicHelp = DuckingDescription("dynamic");
            _effectUpdates.Add(n => header.ToolTip = n.Ducking.Mode == "dynamic" ? dynamicHelp : staticHelp);
        }
        var headerFace = new Border { CornerRadius = new CornerRadius(4, 4, 0, 0), Child = header }; headerFace.SetResourceReference(BackgroundProperty, HeaderBrushKey);
        _title = GraphWorkspace.Text(node.Name, 15, true); _title.Margin = new Thickness(12, 0, 12, 0); _title.VerticalAlignment = VerticalAlignment.Center;
        _title.TextTrimming = TextTrimming.CharacterEllipsis; _title.TextWrapping = TextWrapping.NoWrap; header.Children.Add(_title);
        _rename = new TextBox { Visibility = Visibility.Collapsed, Margin = new Thickness(10, 2, 5, 2), FontSize = 14, MaxLength = 80, Padding = new Thickness(3, 0, 3, 0) }; header.Children.Add(_rename);
        _rename.LostKeyboardFocus += (_, _) => CommitRename();
        _rename.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; } else if (e.Key == Key.Escape) { _rename.Visibility = Visibility.Collapsed; e.Handled = true; } };
        AutomationProperties.SetName(_rename, "Node name");
        header.MouseLeftButtonDown += (_, e) => { if (_rename.IsKeyboardFocusWithin || !IsNodeDragSurface(e.OriginalSource as DependencyObject)) return; if (e.ClickCount == 2) { _rename.Text = _node.Name; _rename.Visibility = Visibility.Visible; _rename.Focus(); _rename.SelectAll(); e.Handled = true; } else _owner.BeginDrag(this, e); };
        _root.Children.Add(headerFace);
        if (node.Kind == "stereoSplit")
        {
            _outputRow = new Grid();
            _outputRow.RowDefinitions.Add(new() { Height = new GridLength(28) });
            _outputRow.RowDefinitions.Add(new() { Height = new GridLength(28) });
            var left = PortRow("Left · mono", true); var right = PortRow("Right · mono", true);
            _outputRow.Children.Add(left); Grid.SetRow(right, 1); _outputRow.Children.Add(right);
            Grid.SetRow(_outputRow, 1); _root.Children.Add(_outputRow);
            AddSocket("audio", true, left, "left"); AddSocket("audio", true, right, "right");
        }
        else if (GraphDefaults.HasAudioOutput(node.Kind) || node.Kind == "control")
        {
            _outputRow = PortRow(node.Kind == "control" ? "Control" : node.Kind is "stereoCombine" or "crossfade" ? "Stereo" : "Audio", true); Grid.SetRow(_outputRow, 1); _root.Children.Add(_outputRow);
            AddSocket(node.Kind == "control" ? "control" : "audio", true, _outputRow);
        }
        if (node.Kind is "stereoCombine" or "crossfade")
        {
            var ports = node.Kind == "stereoCombine" ? new[] { ("left", "Left"), ("right", "Right") } : new[] { ("a", "A · stereo"), ("b", "B · stereo") };
            foreach (var (port, label) in ports)
            { var row = PortRow(label, false); _inputs.Children.Add(row); AddSocket("audio", false, row, port); }
        }
        else if (GraphDefaults.HasAudioInput(node.Kind)) { var row = PortRow(node.Kind == "stereoSplit" ? "Stereo" : "Audio", false); _inputs.Children.Add(row); AddSocket("audio", false, row); }
        if (GraphDefaults.HasSidechainInput(node))
        {
            var row = PortRow("Trigger", false); _inputs.Children.Add(row); AddSocket("sidechain", false, row);
        }
        if (_hasLevelInput)
        {
            var row = PortRow("Level control", false); _inputs.Children.Add(row); AddSocket("control", false, row);
            if (LinkedLevelControl.HasLegacyConnection(owner.Graph, node))
                row.ToolTip = "Existing level link. Use a Mix node for new level control.";
        }
        Grid.SetRow(_inputs, 3); _root.Children.Add(_inputs);
        _status = GraphWorkspace.Text("Not assigned", 13); _status.Height = 18; _status.TextWrapping = TextWrapping.NoWrap;
        _status.TextTrimming = TextTrimming.CharacterEllipsis; _status.Margin = new Thickness(0, 7, 0, 7);
        if (VirtualDeviceBindings.IsVirtual(node.Kind))
        {
            _virtualDevice = GraphWorkspace.Text("", 14);
            _virtualDevice.VerticalAlignment = VerticalAlignment.Center;
            _virtualDevice.TextWrapping = TextWrapping.NoWrap;
            _virtualDevice.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetName(_virtualDevice, "Virtual audio device");
            var deviceName = new Border { Height = 30, Child = _virtualDevice };
            _body.Children.Add(deviceName);
        }
        if (node.Kind is "microphone" or "output")
        {
            _device = Choice("Choose audio device"); _body.Children.Add(_device);
            var deviceItems = new Style(typeof(ComboBoxItem), (Style?)Application.Current.TryFindResource(typeof(ComboBoxItem)));
            deviceItems.Setters.Add(new Setter(IsEnabledProperty, new Binding(nameof(EndpointChoice.IsSelectable))));
            deviceItems.Setters.Add(new Setter(ToolTipProperty, new Binding(nameof(EndpointChoice.Help))));
            deviceItems.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
            deviceItems.Setters.Add(new Setter(AutomationProperties.HelpTextProperty, new Binding(nameof(EndpointChoice.Help))));
            _device.ItemContainerStyle = deviceItems;
            _device.DropDownClosed += (_, _) =>
            {
                bool applying = _applying; _applying = true;
                try { ApplyChoices(_node); } finally { _applying = applying; }
            };
            _device.SelectionChanged += (_, _) =>
            {
                if (_applying || _device.SelectedItem is not EndpointChoice choice) return;
                if (!DeviceChoices.CanAssign(choice, _owner.Mixer.Endpoints, _kind == "microphone" ? "capture" : "render"))
                {
                    _owner.SetMessage("This device is unavailable. Reopen the list to see current devices. " + choice.Help, isError: true);
                    _choiceEndpoints = null; _applying = true;
                    try { ApplyChoices(_node); } finally { _applying = false; }
                    return;
                }
                if (VirtualDeviceBindings.IsUsed(_owner.Graph, Id, new Endpoint { Id = choice.Id, StableId = choice.StableId }))
                { _owner.SetMessage("That output device already has a node. Connect another cable to that output node to combine the audio.", isError: true); _choiceEndpoints = null; _applying = true; try { ApplyChoices(_node); } finally { _applying = false; } return; }
                var binding = choice.Binding(-1);
                _owner.NodeChanged(Id, n => { n.Device = binding.Copy(); n.AutoAssignDevice = VirtualDeviceBindings.IsAssigned(binding); });
            };
            if (node.Kind == "microphone")
            {
                _mono = GraphWorkspace.Button("Mono", "Combine left and right into one centred microphone signal. Off keeps stereo.");
                _mono.ToolTip = NodeToolTip.Create("Mono", "On · Combine left and right", "Off · Keep stereo");
                _mono.Height = 30; _mono.Margin = new Thickness(0, 6, 0, 0);
                AutomationProperties.SetName(_mono, "Mono");
                _mono.Click += (_, _) =>
                {
                    bool mono = !(_node.Mono || _node.Device.Channel >= 0);
                    _owner.NodeChanged(Id, n => { n.Device.Channel = -1; n.Mono = mono; });
                };
                _body.Children.Add(_mono);
            }
        }
        if (node.Kind == "appInput")
        {
            _app = Choice("Choose a running audio application"); _body.Children.Add(_app);
            _app.SelectionChanged += (_, _) =>
            {
                if (_applying || _app.SelectedItem is not AudioApplication app) return;
                if (app.ProcessId != 0 && app.ExecutablePath.Length == 0) { _owner.SetMessage("Windows did not provide this app's identity. Choose another app or use the virtual input.", isError: true); _choiceApplications = null; return; }
                uint pid = app.ProcessId; string path = app.ExecutablePath, name = app.ProcessId == 0 && path.Length == 0 ? "" : app.Name; _owner.NodeChanged(Id, n => n.App = new() { ProcessId = pid, ExecutablePath = path, Name = name });
            };
            _app.ToolTip = NodeToolTip.Create("App capture", "Captures a copy of the app's audio", "Original playback continues", "Same speakers on both paths = doubled audio");
        }
        if (node.Kind is "deEsser" or "plosive" or "compressor" or "limiter" or "ducking" or "expander" or "gate")
        {
            // Reuse the existing status slot: live feedback never changes the
            // node's size or moves a control/socket while audio arrives.
            var feedback = new Grid { Height = _status.Height, Margin = _status.Margin };
            feedback.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            feedback.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            _status.Margin = new Thickness(0); feedback.Children.Add(_status);
            _reductionStrip = new ReductionStrip { Height = 2, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, -4), IsHitTestVisible = false };
            Grid.SetColumnSpan(_reductionStrip, 2);
            feedback.Children.Add(_reductionStrip); _body.Children.Add(feedback);
            _detectorReadout = GraphWorkspace.Text("— dBFS", 11);
            _detectorReadout.SetResourceReference(TextBlock.FontFamilyProperty, "NodeNumericFontFamily");
            _detectorReadout.Width = 92; _detectorReadout.TextAlignment = TextAlignment.Right;
            _detectorReadout.VerticalAlignment = VerticalAlignment.Center;
            _detectorReadout.TextWrapping = TextWrapping.NoWrap;
            _detectorReadout.ToolTip = node.Kind == "ducking" ? "Trigger level" : "Detector level";
            AutomationProperties.SetName(_detectorReadout, node.Kind == "ducking" ? "Trigger level" : "Detector level");
            _detectorReadout.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush");
            Grid.SetColumn(_detectorReadout, 1); feedback.Children.Add(_detectorReadout);
        }
        else _body.Children.Add(_status);
        AddEffects(node.Kind);
        if (node.Kind == "control")
        {
            if (_legacyMultiplierPresentation)
            {
                _legacyMultiplierValue = GraphWorkspace.Text("", 16, true);
                _legacyMultiplierValue.HorizontalAlignment = HorizontalAlignment.Center;
                _legacyMultiplierValue.Margin = new Thickness(0, 12, 0, 12);
                AutomationProperties.SetName(_legacyMultiplierValue, "Saved multiplier");
                _body.Children.Add(_legacyMultiplierValue);
                var useMultiplier = GraphWorkspace.Button("Use multiplier", "Set the multiplier to 100%.");
                AutomationProperties.SetName(useMultiplier, "Use multiplier");
                useMultiplier.Click += (_, _) => _owner.NodeChanged(Id, n => LinkedLevelControl.SetPercent(n, 100));
                _body.Children.Add(useMultiplier);
            }
            else
            {
                _multiplier = new RotaryValueControl("Multiplier", 0, 200, .1, "%", 100,
                    v => _owner.NodeChanged(Id, n => LinkedLevelControl.SetPercent(n, v)), smallChange: 1, largeChange: 10,
                    tile: true, primary: true, format: "0.#",
                    description: "Scales linked levels. 100% is unchanged; 0% is silent.")
                    { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) };
                TrackEditGesture(_multiplier); _body.Children.Add(_multiplier);
            }
        }
        else if (NodeCatalog.HasLevelControl(node.Kind))
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 10) }; divider.SetResourceReference(BackgroundProperty, "ConsoleBorderBrush"); _body.Children.Add(divider);
            _gain = new RotaryValueControl("Level", -60, 12, .1, "dB", GraphDefaults.NewNode(node.Kind, 0, 0).GainDb,
                v => _owner.NodeChanged(Id, n => n.GainDb = v), smallChange: .5, largeChange: 3,
                description: "Adjusts this node's audio level. 0 dB leaves it unchanged.")
                { Margin = new Thickness(0, 0, 0, 6) }; TrackEditGesture(_gain); _body.Children.Add(_gain);
        }
        if (IsDeviceNode(node.Kind))
        {
            _mute = GraphWorkspace.Button("Mute", "Silence this input or output.");
            _mute.FontSize = 13; _mute.Padding = new Thickness(3); _mute.Height = 30; _mute.MinHeight = 30;
            AutomationProperties.SetName(_mute, "Mute");
            _mute.Click += (_, _) => { bool value = !_node.Muted; _owner.NodeChanged(Id, n => n.Muted = value); };
            _body.Children.Add(_mute);
        }
        _meter = new NodeMeter { Width = 28, MinHeight = 90, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch, IsHitTestVisible = false };
        _meterPeakDisplay = new MeterPeakDisplay(_meter);
        bool outputDevice = node.Kind is "output" or "virtualOutput";
        // Outputs have no top Audio socket row. Reserve the same warning space
        // inside their meter column so showing it never moves the node or ports.
        if (outputDevice) _meterPeakDisplay.Margin = new Thickness(0, 28, 0, 0);
        _content.ColumnDefinitions.Add(new() { Width = new GridLength(node.Kind == "control" ? 0 : 36) });
        _content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (node.Kind != "control") _content.Children.Add(_meterPeakDisplay);
        Grid.SetColumn(_body, 1); _content.Children.Add(_body);
        Grid.SetRow(_content, 2); _root.Children.Add(_content);
        if (AvailabilityDeviceElement is not null)
        {
            // A compact triangle with an open exclamation mark occupies the
            // existing space above the meter, without moving bars or controls.
            var symbol = Geometry.Parse("F0 M9,0 L18,16 L0,16 Z M8,5 L10,5 L10,10.5 L8,10.5 Z M8,12 L10,12 L10,14 L8,14 Z");
            symbol.Freeze(); _availabilityWarningIcon.Data = symbol;
            _availabilityWarningIcon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "ConsoleMeterHighBrush");
            if (outputDevice)
            {
                _availabilityWarningIcon.Margin = new Thickness(5, 0, 0, 0);
                _availabilityWarningIcon.VerticalAlignment = VerticalAlignment.Top;
                _content.Children.Add(_availabilityWarningIcon);
            }
            else
            {
                Grid.SetRow(_availabilityWarningIcon, 1); _root.Children.Add(_availabilityWarningIcon);
            }
        }
        if (_effectToggle is not null) { Grid.SetRow(_effectToggle, 4); _root.Children.Add(_effectToggle); }
        // Sockets sit above the header and body so their generous hit targets remain usable.
        var outer = new Grid { ClipToBounds = false };
        _availabilityWarningGlow.SetResourceReference(AvailabilityWarningGlow.GlowBrushProperty, "ConsoleMuteBrush");
        outer.Children.Add(_availabilityWarningGlow); outer.Children.Add(_root);
        _selectionOutline.SetResourceReference(NodeSelectionOutline.StrokeProperty, "NodeSelectedBrush");
        outer.Children.Add(_selectionOutline);
        foreach (var area in new[] { _grabTop, _grabLeft, _grabRight })
        {
            area.MouseLeftButtonDown += (_, e) => _owner.BeginDrag(this, e);
            _grabLayer.Children.Add(area);
        }
        outer.Children.Add(_grabLayer);
        foreach (var socket in _sockets) { var layer = new Canvas { IsHitTestVisible = true }; layer.Children.Add(socket); outer.Children.Add(layer); }
        Child = outer; SizeChanged += (_, _) => { PositionSockets(); UpdateInteractionAdornments(); _owner.UpdateWires(); };
        Unloaded += (_, _) => StopAvailabilityWarningPulse();
        Loaded += (_, _) => UpdateAvailabilityWarningText();
        _root.LayoutUpdated += (_, _) => PositionSockets();
        MouseLeftButtonDown += (_, e) => { if (!e.Handled && IsNodeDragSurface(e.OriginalSource as DependencyObject)) _owner.BeginDrag(this, e); };
        // Passive labels and grab targets keep an ordinary pointer until an
        // actual node drag begins; text editors retain their editing cursor.
        AddHandler(Mouse.QueryCursorEvent, new QueryCursorEventHandler((_, e) =>
        {
            if (CursorForSurface(e.OriginalSource as DependencyObject) is not Cursor cursor) return;
            e.Cursor = cursor; e.Handled = true;
        }), handledEventsToo: true);
        var context = new ContextMenu(); context.Items.Add(new MenuItem { Header = "Copy" }); ContextMenu = context;
        ContextMenuOpening += (_, e) =>
        {
            if (GraphWorkspace.IsTextEditor(e.OriginalSource as DependencyObject)) return;
            _owner.PopulateNodeMenu(context, Id, () => { _rename.Text = _node.Name; _rename.Visibility = Visibility.Visible; _rename.Focus(); _rename.SelectAll(); });
        };
        AutomationProperties.SetName(this, node.Name + " node");
        if (AvailabilityDeviceElement is FrameworkElement source) ToolTipService.SetShowOnDisabled(source, true);
    }


    internal bool CancelPracticeRename()
    {
        if (_rename.Visibility != Visibility.Visible) return false;
        _rename.Visibility = Visibility.Collapsed;
        return true;
    }

    internal bool HasPendingRename => _rename.Visibility == Visibility.Visible;

    internal bool TryCommitPendingRename()
    {
        if (!HasPendingRename) return true;
        if (!_owner.Mixer.CanEdit || !IsEnabled || !_rename.IsEnabled || _rename.IsReadOnly) return false;
        CommitRename();
        return true;
    }

    private Grid PortRow(string name, bool output)
    {
        var row = new Grid { Height = 28, Margin = new Thickness(12, 0, 12, 0) };
        var label = GraphWorkspace.Text(name, 13); label.HorizontalAlignment = output ? HorizontalAlignment.Right : HorizontalAlignment.Left; label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label); return row;
    }
    private static object NodeDescription(string kind) => kind switch
    {
        "mono" => NodeToolTip.Create("Mono", "Combines left and right equally"),
        "channelSwap" => NodeToolTip.Create("Channel Swap", "Exchanges left and right", "Channel levels stay unchanged"),
        "stereoWidth" => NodeToolTip.Create("Stereo Width", "0% · Mono", "100% · Original stereo", "Above 100% · Wider stereo", "Mono input remains mono"),
        "stereoPan" => NodeToolTip.Create("Pan", "Positions a mono signal left to right", "Stereo input is combined to mono first", "Equal-power curve · Centre is −3 dB per side"),
        "crossfade" => NodeToolTip.Create("Crossfade", "Blends stereo inputs A and B", "0% · A only", "100% · B only", "Bypass · A only"),
        "stereoCombine" => NodeToolTip.Create("Stereo Combine", "Left input → Left channel", "Right input → Right channel", "Each input is combined to mono", "An empty input leaves its side silent"),
        "stereoSplit" => NodeToolTip.Create("Stereo Split", "Extracts left and right separately", "Each output is a centred mono signal", "Use Stereo Combine to rebuild stereo"),
        "control" => NodeToolTip.Create("Linked control", "Scales connected levels while keeping their individual settings."),
        "gain" => NodeToolTip.Create("Gain", "Adjusts the incoming level", "0 dB · Unchanged"),
        "mix" => NodeToolTip.Create("Mix", "Combines incoming audio", "Level · Adjusts the combined volume"),
        "highPass" => NodeToolTip.Create("High-pass", "Reduces sound below the cutoff"),
        "lowPass" => NodeToolTip.Create("Low-pass", "Reduces sound above the cutoff"),
        "denoiser" => NodeToolTip.Create("Denoiser", "SpeexDSP: faster, lighter cleanup.", "RNNoise: more delay, usually cleaner speech.", "20/30 ms is added node delay."),
        "deEsser" => NodeToolTip.Create("De-esser", "Tames harsh S and SH sounds", "Acts above the threshold"),
        "plosive" => NodeToolTip.Create("Plosive remover", "Tames low P and B thumps", "Acts above the threshold"),
        "reverb" => NodeToolTip.Create("Reverb", "Adds room-like decay", "Mix · Blends dry sound and reverb"),
        "voiceColor" => NodeToolTip.Create("Voice Color", "Warm / Bright · Tone", "Natural / Broadcast · Character", "Body, clarity and controlled thickening", "Bottom centre · Neutral"),
        "compressor" => NodeToolTip.Create("Compressor", "Reduces its own louder passages", "Ratio controls reduction above threshold"),
        "limiter" => NodeToolTip.Create("Limiter", "Sample-peak ceiling · No lookahead", "Bypass removes limiting", "Later gain can exceed this ceiling"),
        "ducking" => DuckingDescription("static"),
        "expander" => NodeToolTip.Create("Expander", "Reduces quieter passages below the threshold", "Ratio · Reduction below threshold", "Range · Maximum attenuation"),
        "gate" => NodeToolTip.Create("Gate", "Closes below the threshold", "Hold · Keeps the gate open briefly", "Hysteresis · Separate closing threshold", "Range · Maximum attenuation"),
        "alignmentDelay" => NodeToolTip.Create("Alignment Delay", "Delay one path to align it with another", "48 samples = 1 ms · Up to 2 seconds", "Bypass · Undelayed audio"),
        "reroute" => NodeToolTip.Create("Reroute", "A junction for organizing cables"),
        _ => NodeCatalog.Title(kind)
    };
    private static object DuckingDescription(string mode) => NodeToolTip.Create(mode == "dynamic" ? "Dynamic ducking" : "Static ducking",
        mode == "dynamic" ? "Lowers the audio more as the trigger sound gets louder above the threshold."
            : "Lowers the audio by a set amount when the trigger sound crosses the threshold.",
        "Trigger audio is detector only", "Stereo linked");
    internal void SetViewScale(double scale)
    {
        // Zoom belongs to the canvas transform. Never resize fonts, rows,
        // borders or sockets here: all details scale together as one node.
        double next = Math.Clamp(double.IsFinite(scale) ? scale : 1, .3, 2);
        if (_viewScale == next) return;
        _viewScale = next;
        UpdateInteractionAdornments();
    }
    private static Border GrabArea() => new() { Background = Brushes.Transparent, Cursor = Cursors.Arrow };
    private void UpdateInteractionAdornments()
    {
        // Drawing and hit-test overlays live outside normal layout. Their
        // screen-space sizes stay useful without resizing the node contents.
        _selectionOutline.StrokeThickness = 2 / _viewScale;
        _availabilityWarningGlow.ViewScale = _viewScale;
        double top = HeaderGrabTopPadding / _viewScale, side = HeaderGrabSidePadding / _viewScale;
        double width = Math.Max(0, ActualWidth), headerHeight = _root.RowDefinitions[0].Height.Value + 2;
        Canvas.SetLeft(_grabTop, -BorderThickness.Left - side); Canvas.SetTop(_grabTop, -BorderThickness.Top - top);
        _grabTop.Width = width + side * 2; _grabTop.Height = top;
        Canvas.SetLeft(_grabLeft, -BorderThickness.Left - side); Canvas.SetTop(_grabLeft, -BorderThickness.Top);
        _grabLeft.Width = side; _grabLeft.Height = headerHeight;
        Canvas.SetLeft(_grabRight, width - BorderThickness.Left); Canvas.SetTop(_grabRight, -BorderThickness.Top);
        _grabRight.Width = side; _grabRight.Height = headerHeight;
    }
    private void AddSocket(string kind, bool output, FrameworkElement row, string port = "audio")
    {
        var socket = new GraphWorkspace.Socket(_owner, Id, kind, output, port); _sockets.Add(socket); _portAnchors.Add((socket, row));
    }
    private void PositionSockets()
    {
        bool changed = false;
        foreach (var (socket, row) in _portAnchors)
        {
            const double targetWidth = 24, targetHeight = 20;
            socket.Width = targetWidth; socket.Height = targetHeight;
            var layer = (FrameworkElement)socket.Parent;
            Point inset = layer.TranslatePoint(new Point(), this);
            double center = row.TranslatePoint(new Point(0, row.ActualHeight / 2), layer).Y;
            // Read the actual content inset: WPF rounds the border at fractional
            // monitor DPI. Halfway across that inset is the rendered stroke's
            // centre, shared by the glyph and cable anchor.
            double edge = socket.Output ? (ActualWidth + inset.X + layer.ActualWidth) / 2 : inset.X / 2;
            double x = edge - inset.X - socket.Width / 2, y = center - socket.Height / 2;
            if (Canvas.GetLeft(socket) != x || Canvas.GetTop(socket) != y) { Canvas.SetLeft(socket, x); Canvas.SetTop(socket, y); changed = true; }
        }
        if (changed) _socketLayoutDirty = true;
        else if (_socketLayoutDirty) { _socketLayoutDirty = false; _owner.UpdateWires(); }
    }
    public GraphWorkspace.Socket? FindSocket(string kind, bool output, string port = "audio") => _sockets.FirstOrDefault(s => s.Kind == kind && s.Output == output && s.Port == port);
    internal bool IsNodeDragSurface(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current == this) return true;
            if (current is ButtonBase or TextBoxBase or PasswordBox or ComboBox or Slider or Expander
                or EqualizerEditor or VoiceColorPad or RotaryValueControl) return false;
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }
    internal Cursor? CursorForSurface(DependencyObject? source)
    {
        if (IsNodeDragSurface(source)) return _owner.IsNodeDragging ? Cursors.SizeAll : Cursors.Arrow;
        for (var current = source; current is not null && current != this;
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is TextBoxBase text) return text.IsReadOnly ? Cursors.Arrow : Cursors.IBeam;
            if (current is PasswordBox) return Cursors.IBeam;
        }
        // Sliders, buttons, sockets and the EQ plot keep their own cursors.
        return null;
    }
    private ComboBox Choice(string name) { var box = new ComboBox { Height = 30, MinHeight = 30, FontSize = 13, DisplayMemberPath = "Label" }; AutomationProperties.SetName(box, name); _dropDowns.Add(box); return box; }
    private void CommitRename()
    {
        if (_rename.Visibility != Visibility.Visible) return; _rename.Visibility = Visibility.Collapsed; string name = _rename.Text.Trim();
        if (name.Length > 0 && name != _node.Name) _owner.NodeChanged(Id, n => n.Name = name);
    }
    public void SetSelected(bool selected)
    {
        // The ordinary border remains neutral; colouring it as well would add
        // another zoom-scaled stroke underneath the constant-width selection.
        BorderBrush = ConsoleTheme.Get("NodeOutlineBrush");
        _selectionOutline.Visibility = selected && !_owner.Mixer.IsLayoutLocked ? Visibility.Visible : Visibility.Hidden;
    }
    public void Apply(GraphNode node, GraphNodeStatus? status, bool selected)
    {
        _applying = true;
        try
        {
            _node = node; _title.Text = node.Name;
            foreach (var socket in _sockets)
                socket.SetConnected(_owner.Graph.Edges.Any(edge => socket.Output
                    ? edge.From == Id && edge.FromPort == socket.Port && (socket.Kind == "audio" ? edge.Kind is "audio" or "sidechain" : edge.Kind == socket.Kind)
                    : edge.To == Id && edge.ToPort == socket.Port && edge.Kind == socket.Kind));
            _gain?.SetValue(node.GainDb);
            _multiplier?.SetValue(LinkedLevelControl.GetPercent(node));
            if (_legacyMultiplierValue is not null)
                _legacyMultiplierValue.Text = $"Saved multiplier {LinkedLevelControl.GetPercent(node):0.###}%";
            // The legacy Collapsed setting remains serialized, but nodes now
            // always present their complete controls and normal port positions.
            _body.IsEnabled = _owner.Mixer.CanEdit;
            if (_effectToggle is not null) _effectToggle.IsEnabled = _owner.Mixer.CanEdit;
            if (_mute is not null)
            {
                _mute.Background = ConsoleTheme.Get(node.Muted ? "ConsoleMuteBrush" : "ConsoleButtonFaceBrush");
                _mute.Foreground = ConsoleTheme.Get(node.Muted ? "ConsoleInkBrush" : "ConsoleTextBrush");
                _mute.Content = node.Muted ? "Muted" : "Mute";
                AutomationProperties.SetItemStatus(_mute, node.Muted ? "On" : "Off");
            }
            if (_mono is not null)
            {
                bool mono = node.Mono || node.Device.Channel >= 0;
                _mono.Background = ConsoleTheme.Get(mono ? "BrandActionBrush" : "ConsoleButtonFaceBrush");
                _mono.Foreground = ConsoleTheme.Get(mono ? "BrandActionTextBrush" : "ConsoleTextBrush");
                AutomationProperties.SetItemStatus(_mono, mono ? "On" : "Off");
            }
            bool deviceNode = IsDeviceNode(node.Kind);
            bool legacyMuted = !deviceNode && node.Muted;
            string pathLabel = node.Kind == "appInput" && status?.Path.State == "Waiting" ? "Waiting for app" : status?.Path.Display ?? (_owner.Mixer.IsPreview ? "Preview · no audio" : "Connecting");
            _status.Text = !_owner.Mixer.CanEdit ? "Audio engine unavailable" : deviceNode && status?.Path.State != "Running" ? pathLabel + (node.Muted ? " · muted" : "") : legacyMuted ? "Muted · click Bypass to restore audio" : node.Muted ? "Muted" : status?.Clipped == true ? "Clipping · lower the level" : node.Bypass ? "Bypassed · audio passes through" : status?.Path.State == "Running" ? deviceNode ? "Connected" : "Processing" : node.Kind == "control" ? "Linked volume" : node.Kind == "mix" ? "Adds all incoming audio" : "Ready";
            _status.Foreground = ConsoleTheme.Get(status?.Clipped == true && !node.Muted ? "ConsoleAlertTextBrush" : "ConsoleMutedBrush");
            object? statusHelp = status?.Path.Message;
            // Preserve the status row's geometry when an endpoint reconnects,
            // clipping clears or a mute changes. Only its ink disappears.
            _status.Visibility = _virtualDevice is not null || !_owner.Mixer.CanEdit || legacyMuted || deviceNode && status?.Path.State != "Running" || status?.Clipped == true && !node.Muted ? Visibility.Visible : Visibility.Hidden;
            if (node.Kind == "control" && _owner.Mixer.CanEdit && !legacyMuted)
            {
                _status.Text = _legacyMultiplierPresentation ? "Existing level retained" : node.Bypass ? "Multiplier bypassed"
                    : node.ControlMultiplier == 0 ? "Linked levels silenced" : "Linked levels";
                _status.Visibility = _legacyMultiplierPresentation || node.Bypass || node.ControlMultiplier == 0 ? Visibility.Visible : Visibility.Hidden;
                statusHelp = null;
            }
            if (_virtualDevice is not null)
            {
                ApplyVirtualDevice(node);
                if (_owner.Mixer.CanEdit && !VirtualDeviceBindings.IsAssigned(node.Device))
                {
                    var matches = _owner.Mixer.Endpoints.Where(e => VirtualDeviceBindings.Matches(node.Kind, e)).ToArray();
                    _status.Text = matches.Length == 0 ? node.Kind == "virtualInput" ? "Waiting for VB-CABLE" : "Waiting for VAC Lite"
                        : matches.All(e => VirtualDeviceBindings.IsUsed(_owner.Graph, node.Id, e)) ? "Device already has a node"
                        : matches.All(e => e.State != "Active") ? "Waiting for device"
                        : "Choose device · right-click";
                }
                else if (_owner.Mixer.CanEdit && status?.Path.State == "Running" && node.Muted)
                    _status.Text = "Connected · muted";
            }
            if (node.Kind != "control" && _gain is null && node.GainDb != 0 && !legacyMuted && _owner.Mixer.CanEdit && status?.Clipped != true)
            {
                _status.Text = $"Saved level {node.GainDb:0.#} dB · right-click";
                statusHelp = _savedLevelTip ??= NodeToolTip.Create("Saved effect level", "Retained level adjustment from an older version");
                _status.Visibility = Visibility.Visible;
                AutomationProperties.SetItemStatus(this, _status.Text);
            }
            AutomationProperties.SetItemStatus(this, _status.Text);
            bool dynamicsPresentation = _owner.Mixer.CanEdit && !legacyMuted && node.GainDb == 0 && status?.Clipped != true;
            if (_reductionStrip is null || !dynamicsPresentation) _status.ToolTip = statusHelp;
            ApplyDynamicsFeedbackAt(node, status, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, dynamicsPresentation);
            // Older telemetry supplies only one peak; show a single lane until
            // actual channel measurements arrive instead of inventing stereo.
            bool hasStereoTelemetry = status?.PeakLeft is not null && status.PeakRight is not null;
            int channels = hasStereoTelemetry ? status!.Channels : 1;
            bool linkedMute = LinkedLevelSilenced(node);
            bool observePeak = ObservePeakSample(node, status);
            _meter.Update(status?.PeakLeft ?? status?.Peak ?? 0, status?.PeakRight ?? status?.Peak ?? 0, channels,
                status?.Clipped ?? false, node.Muted || linkedMute || !_owner.Mixer.CanEdit || status?.Path.State != "Running",
                _owner.Mixer.ReducedAnimation, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency, observePeak);
            ApplyChoices(node);
            ApplyAvailability(node, status);
            foreach (var update in _effectUpdates) update(node);
            _eqPlot?.ApplySpectrum(status?.SpectrumDb, status?.SpectrumFrame ?? 0, status?.SpectrumRevision ?? 0,
                fresh: _owner.Mixer.CanEdit && status?.Path.State == "Running");
            SetSelected(selected);
        }
        finally { _applying = false; }
    }
    private static (string Device, string Stable, int Channel, bool Mono, uint Process, string App) PeakSource(GraphNode node)
        => (node.Device.Id, node.Device.StableId, node.Device.Channel, node.Mono, node.App.ProcessId, node.App.ExecutablePath);
    private bool LinkedLevelSilenced(GraphNode node)
    {
        bool active = !node.Bypass || IsDeviceNode(node.Kind) || node.Kind is "stereoSplit" or "stereoCombine";
        return _owner.Graph.Edges.Any(edge => edge.Kind == "control" && edge.To == node.Id && !edge.Muted
            && _owner.Graph.Nodes.Any(control => control.Id == edge.From
                && (control.Muted || active && !control.Bypass && control.ControlMultiplier == 0)));
    }
    private bool ObservePeakSample(GraphNode node, GraphNodeStatus? status)
    {
        bool fresh = _peakSampleSequence != _owner.Mixer.MeterSampleSequence || !ReferenceEquals(_peakSampleStatus, status);
        _peakSampleSequence = _owner.Mixer.MeterSampleSequence; _peakSampleStatus = status;
        ulong frames = _owner.Mixer.MeterAudioFrames;
        bool engineRestarted = fresh && _peakFrameKnown && frames < _peakLastAudioFrame;
        if (fresh) { _peakFrameKnown = true; _peakLastAudioFrame = frames; }
        var source = PeakSource(node);
        string endpoint = status?.Path.EndpointId ?? "";
        bool changed = _peakSourceKnown && source != _peakSource
            || endpoint.Length > 0 && _peakEndpointId.Length > 0 && endpoint != _peakEndpointId;
        _peakSourceKnown = true; _peakSource = source;
        if (endpoint.Length > 0) _peakEndpointId = endpoint;
        if (changed || engineRestarted) { _meter.ClearPeakReadings(); _skipPeakSample = true; _awaitPeakFrameAdvance = false; }
        var acknowledged = _owner.Mixer.AcknowledgedGraphNode(node.Id);
        if (acknowledged is null || PeakSource(acknowledged) != source)
        {
            _meter.ClearPeakReadings(); _skipPeakSample = true; _awaitPeakFrameAdvance = false;
            return false;
        }
        // An old audio block can publish its peak after configuration is
        // acknowledged. Wait for frame progress, then drain that packet too;
        // the engine writes meters before advancing its frame counter.
        if (!fresh || status is null) return false;
        if (_skipPeakSample)
        {
            _skipPeakSample = false; _awaitPeakFrameAdvance = true; _peakBoundaryFrame = frames;
            return false;
        }
        if (_awaitPeakFrameAdvance)
        {
            if (frames > _peakBoundaryFrame) _awaitPeakFrameAdvance = false;
            return false;
        }
        return true;
    }
    private void ApplyAvailability(GraphNode node, GraphNodeStatus? status)
    {
        if (AvailabilityDeviceElement is not FrameworkElement source) return;
        var notice = node.Kind is "output" or "virtualOutput"
            ? OutputAvailability.Evaluate(node, status, _owner.Mixer.Endpoints, _owner.Mixer.CanEdit)
            : InputAvailability.Evaluate(node, status, _owner.Mixer.Endpoints, _owner.Mixer.Applications, _owner.Mixer.CanEdit);
        string pathMessage = status?.Path.Message ?? "";
        // Older engine diagnostics use microphone privacy wording for every
        // E_ACCESSDENIED, including playback. Keep output help accurate.
        if (node.Kind is "output" or "virtualOutput" && status?.Path.State == "AccessDenied"
            && pathMessage == "Microphone access blocked; check Windows microphone privacy settings")
            pathMessage = "Output access blocked; check Windows sound device settings";
        bool changed = notice != _availabilityNotice;
        // Keep the original selector and layout. Availability is conveyed by
        // its existing device text, with detail available on hover/accessibility.
        if (!ReferenceEquals(source.ToolTip, _availabilityNoticeHelp)) _availabilityDefaultHelp = source.ToolTip;
        _availabilityNotice = notice;
        if (changed || pathMessage != _availabilityNoticePathMessage)
        {
            _availabilityNoticePathMessage = pathMessage;
            _availabilityNoticeHelp = notice.Kind == AvailabilityNoticeKind.None ? null : NodeToolTip.Create(notice.Label, notice.Detail,
                pathMessage == notice.Detail ? "" : pathMessage);
        }
        source.ToolTip = notice.Kind is AvailabilityNoticeKind.None or AvailabilityNoticeKind.Muted ? _availabilityDefaultHelp : _availabilityNoticeHelp;
        string accessibleStatus = notice.Kind == AvailabilityNoticeKind.None ? "Connected" : notice.Label + ". " + notice.Detail;
        AutomationProperties.SetItemStatus(source, accessibleStatus);
        AutomationProperties.SetHelpText(source, accessibleStatus);
        AutomationProperties.SetLiveSetting(source, AutomationLiveSetting.Off);
        if (notice.Kind != AvailabilityNoticeKind.None)
        {
            // Keep the existing connected/muted status on healthy virtual
            // outputs. Only availability feedback replaces their status ink.
            if (notice.Kind != AvailabilityNoticeKind.Muted || node.Kind is not ("output" or "virtualOutput"))
                _status.Visibility = Visibility.Hidden;
            AutomationProperties.SetItemStatus(this, accessibleStatus);
        }
        _availabilityWarningGlow.Visibility = notice.Kind == AvailabilityNoticeKind.Problem ? Visibility.Visible : Visibility.Hidden;
        _availabilityWarningIcon.Visibility = _availabilityWarningGlow.Visibility;
        UpdateAvailabilityWarningText();
    }
    private void UpdateAvailabilityWarningText()
    {
        if (AvailabilityDeviceElement is not FrameworkElement source) return;
        var property = source is Control ? Control.ForegroundProperty : TextBlock.ForegroundProperty;
        if (_availabilityNotice?.Kind != AvailabilityNoticeKind.Problem)
        {
            StopAvailabilityWarningPulse();
            if (_availabilityTextWarned) source.SetResourceReference(property, "ConsoleTextBrush");
            _availabilityTextWarned = false;
            return;
        }
        Color red = (ConsoleTheme.Get("ConsoleMeterHighBrush") as SolidColorBrush)?.Color ?? Colors.IndianRed;
        Color normal = (ConsoleTheme.Get("ConsoleTextBrush") as SolidColorBrush)?.Color ?? Colors.White;
        bool reduced = _owner.Mixer.ReducedAnimation || !SystemParameters.ClientAreaAnimation;
        bool changed = _availabilityPulseRed != red || _availabilityPulseNormal != normal || _availabilityPulseReduced != reduced;
        if (changed || (!reduced && _availabilityPulseClock is null))
        {
            StopAvailabilityWarningPulse();
            _availabilityPulseRed = red; _availabilityPulseNormal = normal; _availabilityPulseReduced = reduced;
            _availabilityWarningText.Color = red;
            if (!reduced)
            {
                // Only the selected device glyphs change colour. The selector
                // face, arrow, popup choices and node body do not flash or fade.
                var pulse = new ColorAnimation(red, normal, TimeSpan.FromSeconds(AvailabilityWarningPulsePeriodSeconds / 2))
                {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                _availabilityPulseClock = (AnimationClock)pulse.CreateClock(true);
                _availabilityWarningText.ApplyAnimationClock(SolidColorBrush.ColorProperty, _availabilityPulseClock);
            }
        }
        if (!ReferenceEquals(source.GetValue(property), _availabilityWarningText)) source.SetValue(property, _availabilityWarningText);
        _availabilityTextWarned = true;
    }
    private void StopAvailabilityWarningPulse()
    {
        if (_availabilityPulseClock is null) return;
        _availabilityPulseClock?.Controller?.Remove();
        _availabilityPulseClock = null;
        _availabilityWarningText.BeginAnimation(SolidColorBrush.ColorProperty, null);
    }
    internal void SeekAvailabilityWarningPulse(double seconds)
    {
        if (!_owner.Mixer.IsPreview) throw new InvalidOperationException("Only detached preview checks may seek the warning animation.");
        _availabilityPulseClock?.Controller?.SeekAlignedToLastTick(TimeSpan.FromSeconds(seconds), TimeSeekOrigin.BeginTime);
    }
    private static TextBlock? SelectedSourceText(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Popup) continue;
            if (child is TextBlock text && text.FontFamily.Source != "Segoe Fluent Icons") return text;
            if (SelectedSourceText(child) is TextBlock nested) return nested;
        }
        return null;
    }
    internal void ApplyDynamicsFeedbackAt(GraphNode node, GraphNodeStatus? status, double nowSeconds, bool presentationAvailable)
    {
        if (_reductionStrip is null) return;
        if (status is { DynamicsFrame: > 0 } && status.DynamicsFrame != _dynamicsFrame)
        {
            _dynamicsFrame = status.DynamicsFrame; _lastDynamicsTime = nowSeconds;
            _dynamicsReduction = status.GainReductionDb; _dynamicsDetector = status.DetectorDb;
            _dynamicsValid = float.IsFinite(_dynamicsReduction) && float.IsFinite(_dynamicsDetector);
        }
        bool fresh = status is { DynamicsFrame: > 0 } && nowSeconds >= _lastDynamicsTime && nowSeconds - _lastDynamicsTime <= .25;
        bool valid = fresh && status?.Path.State == "Running" && _dynamicsValid;
        bool linkedMute = LinkedLevelSilenced(node);
        VisibleReductionDb = presentationAvailable && valid && !node.Bypass && !node.Muted && !linkedMute
            ? Math.Clamp(_dynamicsReduction, 0, 96) : 0;
        _reductionStrip.Amount = Math.Min(1, VisibleReductionDb / 24);
        _reductionStrip.Visibility = presentationAvailable ? Visibility.Visible : Visibility.Hidden;
        if (!presentationAvailable && _detectorReadout is not null) _detectorReadout.Text = "— dBFS";
        if (!presentationAvailable) return;
        string band = node.Kind == "deEsser" ? "High-band" : node.Kind == "plosive" ? "Low-band" : "Signal";
        bool limiting = node.Kind == "limiter" || node.Kind == "compressor" && node.Compressor.Mode == "limit";
        double threshold = node.Kind == "ducking" ? node.Ducking.ThresholdDb : node.Kind == "deEsser" ? node.DeEsser.ThresholdDb : node.Kind == "plosive" ? node.Plosive.ThresholdDb : node.Kind is "expander" or "gate" ? node.Expander.ThresholdDb : limiting ? node.Compressor.CeilingDb : node.Compressor.ThresholdDb;
        _status.Text = node.Muted || linkedMute ? "Muted" : node.Bypass ? "Bypassed" : !valid ? "Waiting for audio" : VisibleReductionDb >= .05
            ? $"Reduction {VisibleReductionDb:0.0} dB" : node.Kind is "expander" or "gate" ? "Open · Reduction 0.0 dB" : _dynamicsDetector <= threshold ? "Below threshold" : "Reduction 0.0 dB";
        if (node.Kind == "ducking" && !node.Bypass && !node.Muted && !linkedMute)
            _status.Text = !_owner.Graph.Edges.Any(edge => edge.Kind == "sidechain" && edge.To == node.Id && !edge.Muted)
                ? "Connect a trigger" : !valid ? "Waiting for trigger audio" : VisibleReductionDb >= .05
                ? "Ducking" : node.Ducking.Mode == "dynamic" && node.Ducking.Ratio == 1 ? "No attenuation · ratio 1:1"
                : node.Ducking.Mode != "dynamic" && node.Ducking.AmountDb == 0 ? "No attenuation · amount 0 dB" : "Below threshold";
        string detectorLine = $"{(node.Kind == "ducking" ? "Trigger" : "Detector")} · {(valid ? _dynamicsDetector.ToString("0.0") : "—")} dBFS";
        if (_detectorReadout is not null)
        {
            _detectorReadout.Text = $"{(valid ? _dynamicsDetector.ToString("0.0") : "—")} dBFS";
            AutomationProperties.SetHelpText(_detectorReadout, detectorLine);
        }
        string thresholdLine = $"{(limiting ? "Ceiling" : "Threshold")} · {threshold:0.0} dBFS";
        if (_dynamicsTip is null)
        {
            _dynamicsTip = NodeToolTip.Create(band + " reduction", detectorLine, thresholdLine,
                node.Kind is "compressor" or "limiter" or "ducking" or "expander" or "gate" ? "Gain reduction · Bar 0–24 dB" : "Shelf amount · Bar 0–24 dB");
        }
        else
        {
            // Refresh live values in place. A changing detector must not build
            // another panel and four WPF text elements on every meter sample.
            ((TextBlock)_dynamicsTip.Children[1]).Text = detectorLine;
            ((TextBlock)_dynamicsTip.Children[2]).Text = thresholdLine;
        }
        _status.ToolTip = _dynamicsTip;
        _status.Visibility = Visibility.Visible;
        AutomationProperties.SetItemStatus(this, _status.Text);
    }
    private void ApplyChoices(GraphNode node)
    {
        if (_device is not null)
        {
            var inventory = _owner.Mixer.Endpoints;
            var visibility = DeviceVisibility.For(_owner.Mixer);
            var bindingKey = (node.Device.Id, node.Device.StableId, node.Device.Name, node.Device.Channel);
            // The model retains an inventory instance until its full metadata
            // changes. Meter telemetry must not enumerate/stringify all devices
            // for every node or replace an open chooser beneath the pointer.
            if (!_device.IsDropDownOpen && (!ReferenceEquals(_choiceEndpoints, inventory) || _deviceBindingKey != bindingKey || _deviceVisibility != visibility))
            {
                string direction = node.Kind == "microphone" ? "capture" : "render";
                _choiceEndpoints = inventory; _deviceBindingKey = bindingKey; _deviceVisibility = visibility; DeviceChoiceBuildCount++;
                var choices = DeviceChoices.Build(inventory, direction, node.Device, visibility);
                _device.ItemsSource = choices; _device.SelectedItem = DeviceChoices.Selected(choices, node.Device);
                AutomationProperties.SetHelpText(_device, "Choose an available audio device. Additional devices: Settings → Audio device visibility.");
            }
        }
        if (_app is not null)
        {
            var inventory = _owner.Mixer.Applications;
            var bindingKey = (node.App.ProcessId, node.App.ExecutablePath, node.App.Name);
            if (!_app.IsDropDownOpen && (!ReferenceEquals(_choiceApplications, inventory) || _appBindingKey != bindingKey))
            {
                _choiceApplications = inventory; _appBindingKey = bindingKey; AppChoiceBuildCount++;
                var applications = inventory.OrderBy(a => a.Name).ToList();
                var selected = applications.FirstOrDefault(a => node.App.ProcessId != 0 && a.ProcessId == node.App.ProcessId && (node.App.ExecutablePath.Length == 0 || string.Equals(a.ExecutablePath, node.App.ExecutablePath, StringComparison.OrdinalIgnoreCase)));
                var sameExecutable = applications.Where(a => node.App.ExecutablePath.Length > 0 && string.Equals(a.ExecutablePath, node.App.ExecutablePath, StringComparison.OrdinalIgnoreCase)).ToList();
                if (selected is null && sameExecutable.Count == 1) selected = sameExecutable[0];
                // The picker inventory is narrower than the capture worker's
                // process list. Only path telemetry can diagnose availability.
                if (selected is null && (node.App.ProcessId != 0 || node.App.ExecutablePath.Length > 0)) { selected = new() { ProcessId = node.App.ProcessId, ExecutablePath = node.App.ExecutablePath, Name = node.App.Name + " · saved selection" }; applications.Add(selected); }
                applications.Insert(0, new() { Name = "Choose a running app" }); _app.ItemsSource = applications; _app.SelectedItem = selected ?? applications[0];
            }
        }
    }
    private void ApplyVirtualDevice(GraphNode node)
    {
        var inventory = _owner.Mixer.Endpoints;
        var bindingKey = (node.Device.Id, node.Device.StableId, node.Device.Name, node.Device.Channel);
        if (ReferenceEquals(_choiceEndpoints, inventory) && _deviceBindingKey == bindingKey) return;
        _choiceEndpoints = inventory; _deviceBindingKey = bindingKey;
        var endpoint = inventory.FirstOrDefault(e => VirtualDeviceBindings.Matches(node.Kind, e) && VirtualDeviceBindings.Same(node.Device, e));
        _virtualDevice!.Text = endpoint?.Name is { Length: > 0 } name ? name
            : node.Device.Name.Length > 0 ? node.Device.Name
            : node.Kind == "virtualInput" ? "CABLE Output" : "Line 1";
        _virtualDevice.ToolTip = NodeToolTip.Create(_virtualDevice.Text,
            node.Kind == "virtualInput" ? "Receives audio sent to the matching CABLE Input playback device"
                : "Sends audio to the matching Line 1 recording device for Discord or games");
    }
    private static bool IsDeviceNode(string kind) => kind is "microphone" or "virtualInput" or "appInput" or "output" or "virtualOutput";

    private void AddEffects(string kind)
    {
        if (!IsDeviceNode(kind) && kind is not ("stereoSplit" or "stereoCombine"))
        {
            var bypass = _effectToggle = GraphWorkspace.Button("Bypass", "Pass audio through without this node's processing or level adjustment.");
            bypass.Height = bypass.MinHeight = 36; bypass.FontSize = 14; bypass.FontWeight = FontWeights.SemiBold;
            bypass.Margin = new Thickness(12, 0, 12, 12); bypass.BorderThickness = new Thickness(1.5);
            AutomationProperties.SetName(bypass, "Bypass");
            // Keep the state fill and text contrast while hovering. The shared
            // button template replaces its fill, which would obscure this state.
            bypass.Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
                  <Border x:Name="Face" CornerRadius="3" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}">
                    <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource ConsoleTextBrush}"/></Trigger>
                    <Trigger Property="IsPressed" Value="True"><Setter Property="Opacity" Value="0.8"/></Trigger>
                    <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.55"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
                """);
            bypass.Click += (_, _) =>
            {
                // A saved mute from an older version stays silent until the
                // user deliberately restores it here. Never hide an active
                // legacy mute behind an apparently working bypass control.
                bool value = _node.Muted || !_node.Bypass;
                _owner.NodeChanged(Id, n => { n.Muted = false; n.Bypass = value; });
            };
            var previouslyMutedTip = NodeToolTip.Create("Previously muted", "Restores audio with this node's processing bypassed");
            _effectUpdates.Add(n =>
            {
                bypass.Background = ConsoleTheme.Get(n.Bypass ? "ConsoleMuteBrush" : "ConsoleButtonFaceBrush");
                bypass.Foreground = ConsoleTheme.Get(n.Bypass ? "ConsoleInkBrush" : "ConsoleTextBrush");
                bypass.BorderBrush = ConsoleTheme.Get(n.Bypass ? "ConsoleMuteBrush" : "ConsoleMutedBrush");
                bypass.ToolTip = n.Muted ? previouslyMutedTip
                    : kind == "control" ? n.Bypass ? "Apply the multiplier again." : "Leave linked levels unchanged."
                    : n.Bypass ? "Restore this node's processing and level adjustment."
                    : kind == "crossfade" ? "Play input A only, without blending input B."
                    : "Pass audio through without this node's processing or level adjustment.";
                AutomationProperties.SetItemStatus(bypass, n.Muted ? "Legacy muted" : n.Bypass ? "On" : "Off");
            });
        }
        if (kind == "eq") BuildEq();
        if (kind == "voiceColor") BuildVoiceColor();
        if (kind == "tone") BuildTone();
        if (kind is "highPass" or "lowPass") BuildPassFilter(kind);
        if (kind == "compressor") BuildCompressor();
        if (kind == "limiter") BuildLimiter();
        if (kind == "ducking") BuildDucking();
        if (kind is "expander" or "gate") BuildExpander();
        if (kind == "alignmentDelay")
        {
            EffectNumber("Milliseconds", 0, 2000, .001, "ms", n => n.AlignmentDelay.Milliseconds, (n, v) => n.AlignmentDelay.Samples = AlignmentDelaySettings.FromMilliseconds(v), primary: true);
            EffectNumber("Samples", 0, 96000, 1, "", n => n.AlignmentDelay.Samples, (n, v) => n.AlignmentDelay.Samples = (int)v, integer: true);
            var help = GraphWorkspace.Text("48 kHz · 48 samples = 1 ms", 12); help.Margin = new Thickness(0, 3, 0, 7); _body.Children.Add(help);
        }
        if (kind == "pan") { StartDialGroup(1); EffectNumber("L / R", -1, 1, .01, "", n => n.Balance, (n, v) => n.Balance = v, primary: true); }
        if (kind == "stereoWidth")
        {
            StartDialGroup(1); EffectNumber("Width", 0, 200, 1, "%", n => n.StereoWidth * 100, (n, v) => n.StereoWidth = v / 100, primary: true);
            AddNote("0% Mono · 100% Original · 200% Wide");
        }
        if (kind == "stereoPan")
        {
            StartDialGroup(1); EffectNumber("L / R", -1, 1, .01, "", n => n.PanPosition, (n, v) => n.PanPosition = v, primary: true);
            AddNote("Stereo input is combined to mono.");
        }
        if (kind == "crossfade")
        {
            var curve = EffectChoice("Crossfade curve", [("equalPower", "Equal power"), ("linear", "Linear")], n => n.CrossfadeCurve, (n, value) => n.CrossfadeCurve = value);
            curve.ToolTip = NodeToolTip.Create("Fade curve", "Equal power · Different sources", "Linear · Matching signals", "Delayed paths may need Alignment Delay");
            StartDialGroup(1); EffectNumber("A / B", 0, 100, 1, "%", n => n.CrossfadePosition * 100, (n, v) => n.CrossfadePosition = v / 100, primary: true);
            AddNote("0% A only · 100% B only");
        }
        if (kind == "channelSwap") AddNote("Left → Right\nRight → Left");
        if (kind == "stereoCombine") AddNote("Each input becomes one channel.\nAn empty side stays silent.");
        if (kind == "stereoSplit") AddNote("Each output is a mono signal.");
        if (kind == "denoiser")
        {
            var algorithmLabel = GraphWorkspace.Text("Algorithm", 12, true);
            algorithmLabel.Margin = new Thickness(0, 0, 0, 4); _body.Children.Add(algorithmLabel);
            var algorithm = EffectChoice("Denoiser algorithm", [("speex", "SpeexDSP · 20 ms"), ("rnnoise", "RNNoise · 30 ms")],
                n => n.Denoiser.Algorithm, (n, value) => n.Denoiser.Algorithm = value);
            algorithm.ToolTip = NodeToolTip.Create("Algorithm", "SpeexDSP: faster, lighter cleanup.", "RNNoise: more delay, usually cleaner speech.", "20/30 ms is added node delay.");
            AutomationProperties.SetHelpText(algorithm, "SpeexDSP: faster, lighter cleanup, 20 milliseconds. RNNoise: more delay, usually cleaner speech, 30 milliseconds. VineMix node delay including bypass, not total system latency.");
            StartDialGroup(1);
            EffectNumber("Reduction", 0, 40, 1, "dB", n => -n.Denoiser.SuppressionDb, (n, v) => n.Denoiser.SuppressionDb = -v, primary: true);
            var spectral = _dialGroup!; _body.Children.Remove(spectral);
            StartDialGroup(1);
            EffectNumber("Mix", 0, 100, 1, "%", n => n.Denoiser.RnnoiseMix * 100, (n, v) => n.Denoiser.RnnoiseMix = v / 100, primary: true);
            var neural = _dialGroup!; _body.Children.Remove(neural);
            var controls = new Grid(); controls.Children.Add(spectral); controls.Children.Add(neural); _body.Children.Add(controls);
            var delay = GraphWorkspace.Text("", 12); delay.Margin = new Thickness(0, 0, 0, 5); _body.Children.Add(delay);
            _effectUpdates.Add(n =>
            {
                bool selectedNeural = n.Denoiser.Algorithm == "rnnoise";
                spectral.Visibility = selectedNeural ? Visibility.Hidden : Visibility.Visible;
                neural.Visibility = selectedNeural ? Visibility.Visible : Visibility.Hidden;
                spectral.IsEnabled = !selectedNeural; neural.IsEnabled = selectedNeural;
                delay.Text = selectedNeural ? "Delay · 30 ms, including bypass" : "Delay · 20 ms, including bypass";
            });
            _dialGroup = null;
        }
        if (kind == "deEsser")
        {
            EffectNumber("Frequency", 2000, 12000, 10, "Hz", n => n.DeEsser.FrequencyHz, (n, v) => n.DeEsser.FrequencyHz = v, true);
            EffectNumber("Threshold", -60, 0, 1, "dBFS", n => n.DeEsser.ThresholdDb, (n, v) => n.DeEsser.ThresholdDb = v, primary: true);
            StartDialGroup(2);
            EffectNumber("Max reduction", 0, 24, 1, "dB", n => n.DeEsser.ReductionDb, (n, v) => n.DeEsser.ReductionDb = v);
        }
        if (kind == "plosive")
        {
            EffectNumber("Frequency limit", 60, 300, 1, "Hz", n => n.Plosive.FrequencyHz, (n, v) => n.Plosive.FrequencyHz = v, true);
            EffectNumber("Threshold", -60, 0, 1, "dBFS", n => n.Plosive.ThresholdDb, (n, v) => n.Plosive.ThresholdDb = v, primary: true);
            StartDialGroup(2);
            EffectNumber("Max reduction", 0, 24, 1, "dB", n => n.Plosive.ReductionDb, (n, v) => n.Plosive.ReductionDb = v);
        }
        if (kind == "reverb")
        {
            EffectNumber("Mix", 0, 100, 1, "%", n => n.Reverb.Mix * 100, (n, v) => n.Reverb.Mix = v / 100, primary: true);
            EffectNumber("Decay", .1, 5, .1, "s", n => n.Reverb.DecaySeconds, (n, v) => n.Reverb.DecaySeconds = v, true);
            StartDialGroup(2);
            EffectNumber("Damping", 0, 100, 1, "%", n => n.Reverb.Damping * 100, (n, v) => n.Reverb.Damping = v / 100);
            EffectNumber("Pre-delay", 0, 100, 1, "ms", n => n.Reverb.PreDelayMs, (n, v) => n.Reverb.PreDelayMs = v);
        }
    }
    private ComboBox EffectChoice(string name, (string Value, string Label)[] choices, Func<GraphNode, string> get, Action<GraphNode, string> set)
    {
        var choice = Choice(name); choice.DisplayMemberPath = ""; choice.Margin = new Thickness(0, 0, 0, 5);
        foreach (var item in choices) choice.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Value });
        choice.SelectionChanged += (_, _) => { if (!_applying && choice.SelectedItem is ComboBoxItem { Tag: string value }) _owner.NodeChanged(Id, node => set(node, value)); };
        _effectUpdates.Add(node => choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, get(node))));
        _body.Children.Add(choice); return choice;
    }
    private void BuildCompressor()
    {
        // Missing presentation version means a saved legacy processor. No audio
        // settings are inferred or rewritten to simplify its interface.
        bool legacy = _node.DynamicsUiVersion == 0;
        if (legacy)
        {
            EffectChoice("Compressor mode", [("compress", "Compress"), ("limit", "Limit")], n => n.Compressor.Mode, (n, value) => n.Compressor.Mode = value);
        }
        int start = _body.Children.Count;
        StartDialGroup(3);
        EffectNumber("Threshold", -60, 0, .1, "dBFS", n => n.Compressor.ThresholdDb, (n, v) => n.Compressor.ThresholdDb = v, primary: true);
        EffectNumber("Ratio", 1, 20, .1, ":1", n => n.Compressor.Ratio, (n, v) => n.Compressor.Ratio = v);
        EffectNumber("Make-up", 0, 24, .1, "dB", n => n.Compressor.MakeupDb, (n, v) => n.Compressor.MakeupDb = v);
        StartDialGroup(3);
        EffectNumber("Attack", .1, 200, .1, "ms", n => n.Compressor.AttackMs, (n, v) => n.Compressor.AttackMs = v, true);
        EffectNumber("Release", 5, 2000, 1, "ms", n => n.Compressor.ReleaseMs, (n, v) => n.Compressor.ReleaseMs = v, true);
        if (!legacy) return;
        var compression = new StackPanel();
        while (_body.Children.Count > start) { var child = _body.Children[start]; _body.Children.RemoveAt(start); compression.Children.Add(child); }
        _dialGroup = null;
        BuildLimiter();
        var limiting = new StackPanel();
        while (_body.Children.Count > start) { var child = _body.Children[start]; _body.Children.RemoveAt(start); limiting.Children.Add(child); }
        // Both groups contribute their desired size even when hidden, keeping
        // saved mode changes from moving ports or the Bypass button.
        var area = new Grid(); area.Children.Add(compression); area.Children.Add(limiting); _body.Children.Add(area);
        _effectUpdates.Add(node => {
            bool limit = node.Compressor.Mode == "limit";
            compression.Visibility = limit ? Visibility.Hidden : Visibility.Visible;
            limiting.Visibility = limit ? Visibility.Visible : Visibility.Hidden;
            compression.IsEnabled = !limit; limiting.IsEnabled = limit;
        });
        _dialGroup = null;
    }
    private void BuildLimiter()
    {
        StartDialGroup(2);
        EffectNumber("Ceiling", -24, 0, .1, "dBFS", n => n.Compressor.CeilingDb, (n, v) => n.Compressor.CeilingDb = v, primary: true);
        EffectNumber("Release", 5, 2000, 1, "ms", n => n.Compressor.ReleaseMs, (n, v) => n.Compressor.ReleaseMs = v, true);
        AddNote("Sample peaks · No lookahead\nBypass removes limiting.\nLater gain can exceed this ceiling.");
    }
    private void BuildDucking()
    {
        var mode = EffectChoice("Ducking mode", [("static", "Static ducking"), ("dynamic", "Dynamic ducking")],
            n => n.Ducking.Mode, (n, value) => n.Ducking.Mode = value);
        foreach (var item in mode.Items.OfType<ComboBoxItem>()) item.ToolTip = DuckingDescription((string)item.Tag);
        var staticHelp = DuckingDescription("static"); var dynamicHelp = DuckingDescription("dynamic");
        StartDialGroup(2);
        EffectNumber("Threshold", -60, 0, .1, "dBFS", n => n.Ducking.ThresholdDb, (n, v) => n.Ducking.ThresholdDb = v);
        var amount = EffectNumber("Duck amount", 0, 60, .1, "dB", n => n.Ducking.AmountDb, (n, v) => n.Ducking.AmountDb = v, primary: true);
        var ratio = EffectNumber("Ratio", 1, 20, .1, ":1", n => n.Ducking.Ratio, (n, v) => n.Ducking.Ratio = v, primary: true);
        // Overlay the alternative controls in the same measured slot. Hidden
        // controls retain their size and saved values across either mode.
        _dialGroup!.Children.Remove(amount); _dialGroup.Children.Remove(ratio);
        var depth = new Grid(); depth.Children.Add(amount); depth.Children.Add(ratio); _dialGroup.Children.Add(depth);
        StartDialGroup(2);
        EffectNumber("Attack", .1, 200, .1, "ms", n => n.Ducking.AttackMs, (n, v) => n.Ducking.AttackMs = v, true);
        EffectNumber("Release", 5, 2000, 1, "ms", n => n.Ducking.ReleaseMs, (n, v) => n.Ducking.ReleaseMs = v, true);
        _effectUpdates.Add(node =>
        {
            bool dynamic = node.Ducking.Mode == "dynamic";
            amount.Visibility = dynamic ? Visibility.Hidden : Visibility.Visible;
            ratio.Visibility = dynamic ? Visibility.Visible : Visibility.Hidden;
            amount.IsEnabled = !dynamic; ratio.IsEnabled = dynamic;
            mode.ToolTip = dynamic ? dynamicHelp : staticHelp;
        });
    }
    private void AddNote(string text)
    {
        var note = GraphWorkspace.Text(text, 11); note.Margin = new Thickness(0, 3, 0, 6);
        note.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleMutedBrush"); _body.Children.Add(note);
    }
    private void StartDialGroup(int columns)
    {
        // Labels on the dials already name each parameter. Spacing provides
        // grouping without repeating a second hierarchy of tiny headings.
        _dialGroup = new UniformGrid { Columns = columns, Margin = new Thickness(0, 6, 0, 3) };
        _dialGroupHeight = 58;
        _body.Children.Add(_dialGroup);
    }
    private void BuildExpander()
    {
        StartDialGroup(3);
        EffectNumber("Threshold", -80, 0, .5, "dBFS", n => n.Expander.ThresholdDb, (n, v) => n.Expander.ThresholdDb = v, primary: true);
        if (_gatePresentation)
        {
            EffectNumber("Hysteresis", 0, 12, .1, "dB", n => n.Expander.HysteresisDb, (n, v) => n.Expander.HysteresisDb = v);
        }
        else
        {
            EffectNumber("Ratio", 1, 20, .1, ":1", n => n.Expander.Ratio, (n, v) => n.Expander.Ratio = v);
        }
        EffectNumber("Range", 0, 80, .5, "dB", n => n.Expander.RangeDb, (n, v) => n.Expander.RangeDb = v);
        StartDialGroup(3);
        EffectNumber("Attack", .1, 200, .1, "ms", n => n.Expander.AttackMs, (n, v) => n.Expander.AttackMs = v, true);
        if (_gatePresentation)
        {
            EffectNumber("Hold", 0, 1000, 1, "ms", n => n.Expander.HoldMs, (n, v) => n.Expander.HoldMs = v);
        }
        EffectNumber("Release", 5, 2000, 1, "ms", n => n.Expander.ReleaseMs, (n, v) => n.Expander.ReleaseMs = v, true);
    }
    private void BuildPassFilter(string kind)
    {
        var defaults = EqBand.FilterDefaults(kind)[0];
        StartDialGroup(2);
        EffectNumber("Cutoff", 20, 20000, 1, "Hz", n => n.EqBands.Count == 1 ? n.EqBands[0].FrequencyHz : defaults.FrequencyHz,
            (n, value) => { if (n.EqBands.Count == 1) n.EqBands[0].FrequencyHz = value; }, true, primary: true);
        EffectNumber("Resonance / Q", .1, 20, .01, "", n => n.EqBands.Count == 1 ? n.EqBands[0].Q : defaults.Q,
            (n, value) => { if (n.EqBands.Count == 1) n.EqBands[0].Q = value; }, true);
    }
    private void BuildVoiceColor()
    {
        var pad = new VoiceColorPad { Margin = new Thickness(0, 0, 0, 6) };
        pad.EditGestureStarted += BeginControlEditGesture;
        pad.EditGestureEnded += EndControlEditGesture;
        pad.PositionEdited += (tone, ambience) =>
        {
            if (_applying) return;
            // Commit both coordinates together through the existing editor queue.
            _owner.NodeChanged(Id, node => { node.VoiceColor.Tone = tone; node.VoiceColor.Ambience = ambience; });
        };
        _body.Children.Add(pad);
        _effectUpdates.Add(node => pad.Apply(node.VoiceColor.Tone, node.VoiceColor.Ambience, node.Bypass));
        EffectNumber("Tone", -100, 100, .1, "%", node => node.VoiceColor.Tone * 100,
            (node, value) => node.VoiceColor.Tone = value / 100);
        EffectNumber("Character", 0, 100, .1, "%", node => node.VoiceColor.Ambience * 100,
            (node, value) => node.VoiceColor.Ambience = value / 100);
    }
    private RotaryValueControl EffectNumber(string label, double min, double max, double step, string suffix, Func<GraphNode, double> get, Action<GraphNode, double> set, bool logarithmic = false, bool integer = false, bool primary = false)
    {
        if (_dialGroup is null) StartDialGroup(2);
        var control = new RotaryValueControl(label, min, max, step, suffix, get(GraphDefaults.NewNode(_kind, 0, 0)),
            v => _owner.NodeChanged(Id, n => set(n, v)), logarithmic: logarithmic, tile: true, integer: integer,
            format: suffix is "ms" or "s" ? "0.###" : suffix == ":1" ? "0.0##" : label.Contains("Q") ? "0.0###" : null,
            primary: primary, description: EffectDescription(label));
        TrackEditGesture(control);
        if (_dialGroup!.Columns == 3) control.Width = 120;
        _dialGroup!.Children.Add(control); _effectUpdates.Add(n => control.SetValue(get(n)));
        _dialGroupHeight = Math.Max(_dialGroupHeight, primary ? 86 : 58);
        foreach (RotaryValueControl peer in _dialGroup.Children) peer.SetDialRowHeight(_dialGroupHeight);
        return control;
    }
    private string EffectDescription(string label) => (_kind, label) switch
    {
        ("alignmentDelay", _) => "Delays this audio path to align it with another.",
        ("pan", _) => "Balances audio between the left and right channels.",
        ("stereoWidth", _) => "Sets stereo width. 0% is mono; 100% is unchanged. Wider settings can raise peaks. Mono input remains mono.",
        ("stereoPan", _) => "Moves a mono signal from left (-1) to centre (0) to right (1). Stereo input is combined to mono first; centre is -3 dB per side.",
        ("crossfade", _) => "Blends stereo inputs A and B. 0% plays A only; 100% plays B only. Bypass plays A only.",
        ("denoiser", "Reduction") => "Sets how strongly SpeexDSP reduces steady background noise.",
        ("denoiser", "Mix") => "Blends original and RNNoise audio. 0% is original; 100% is fully denoised.",
        ("deEsser", "Frequency") => "Sets the frequency of the harsh S and SH sounds to reduce.",
        ("deEsser", "Threshold") => "Sets the level above which harsh S and SH sounds are reduced.",
        ("plosive", "Frequency limit") => "Sets the upper frequency of the low P and B thumps to reduce.",
        ("plosive", "Threshold") => "Sets the level above which low P and B thumps are reduced.",
        (_, "Max reduction") => "Limits the amount of attenuation applied to detected sounds.",
        ("reverb", "Mix") => "Blends original audio with the reverberated sound.",
        ("reverb", "Decay") => "Sets how long the reverberation lasts.",
        ("reverb", "Damping") => "Softens high frequencies in the reverb tail.",
        ("reverb", "Pre-delay") => "Sets the gap before reverberation begins.",
        ("compressor", "Threshold") => "Sets the level above which louder passages are reduced.",
        ("compressor", "Ratio") => "Sets how strongly audio above the threshold is reduced.",
        ("compressor", "Make-up") => "Adds level after compression to compensate for the reduction.",
        (_, "Ceiling") => "Sets the sample-peak limit.",
        ("ducking", "Threshold") => "Sets the trigger level above which ducking reduces the main audio.",
        ("ducking", "Duck amount") => "Sets the fixed reduction while the trigger is above the threshold.",
        ("ducking", "Ratio") => "Sets how strongly the main audio is reduced as the trigger rises above the threshold.",
        ("ducking", "Attack") => "Sets how quickly the main audio becomes quieter when the trigger is active.",
        ("ducking", "Release") => "Sets how quickly the main audio returns to its original level after the trigger falls.",
        ("expander" or "gate", "Threshold") => _gatePresentation
            ? "Sets the signal level that opens the gate."
            : "Sets the level below which quieter passages are reduced.",
        ("expander" or "gate", "Ratio") => "Sets how strongly audio below the threshold is reduced.",
        ("expander" or "gate", "Hysteresis") => "Closes the gate this many dB below its opening threshold.",
        ("expander" or "gate", "Range") => "Limits how far the signal can be turned down.",
        ("expander" or "gate", "Attack") => "Sets how quickly the signal returns to full level above the threshold.",
        ("expander" or "gate", "Hold") => "Keeps the gate open briefly after the signal drops.",
        ("expander" or "gate", "Release") => "Sets how quickly the signal becomes quieter below the threshold.",
        ("highPass", "Cutoff") => "Sets the frequency below which sound is reduced.",
        ("lowPass", "Cutoff") => "Sets the frequency above which sound is reduced.",
        (_, "Resonance / Q") => "Sets the emphasis around the cutoff frequency.",
        ("voiceColor", "Tone") => "Shifts the voice from warm to bright.",
        ("voiceColor", "Character") => "Adds a fuller broadcast character to the voice.",
        (_, "Attack") => "Sets how quickly gain reduction responds to louder audio.",
        (_, "Release") => "Sets how quickly the audio returns to its original level after gain reduction.",
        _ => "Adjusts " + label.ToLowerInvariant() + "."
    };
    private void BuildTone()
    {
        var dials = new StackPanel { Margin = new Thickness(0, 5, 0, 8) };
        string[] labels = ["Low", "Mid", "High"];
        string[] ranges = ["200 Hz shelf", "1 kHz bell", "5 kHz shelf"];
        for (int band = 0; band < 3; band++)
        {
            int index = band;
            var dial = new RotaryGainControl(labels[band], ranges[band], value =>
                _owner.NodeChanged(Id, n => { if (n.EqBands.Count > index) n.EqBands[index].GainDb = value; }));
            TrackEditGesture(dial);
            dials.Children.Add(dial);
            _effectUpdates.Add(n => dial.SetValue(n.EqBands.Count > index ? n.EqBands[index].GainDb : 0));
        }
        _body.Children.Add(dials);
    }

    private void BuildEq()
    {
        int selected = 0, bandCount = -1; bool updating = false;
        var plot = _eqPlot = new EqualizerEditor { Height = 300, Margin = new Thickness(0, 0, 0, 7) }; _body.Children.Add(plot);
        plot.EditGestureStarted += BeginControlEditGesture;
        plot.EditGestureEnded += EndControlEditGesture;
        var details = new Grid { Margin = new Thickness(0, 0, 0, 2) }; _body.Children.Add(details);
        for (int i = 0; i < 3; i++) details.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        void EditBand(Action<EqBand> edit)
        {
            int index = selected;
            _owner.NodeChanged(Id, n =>
            {
                if (index < 0 || index >= n.EqBands.Count) return;
                var band = n.EqBands[index];
                if (band.ResetFrequencyHz == 0) band.ResetFrequencyHz = band.FrequencyHz;
                edit(band);
            });
        }
        var frequency = new RotaryValueControl("Frequency", 20, 20000, 1, "Hz", new EqBand().FrequencyHz,
            v => EditBand(b => b.FrequencyHz = v), logarithmic: true, compact: true, smallChange: 10, largeChange: 100, primary: true);
        var gain = new RotaryValueControl("Band gain", -24, 24, .1, "dB", 0,
            v => EditBand(b => b.GainDb = v), compact: true, smallChange: .5, largeChange: 3, primary: true);
        var q = new RotaryValueControl("Width / Q", .1, 20, .01, "", new EqBand().Q,
            v => EditBand(b => b.Q = v), logarithmic: true, compact: true, smallChange: .1, largeChange: 1, format: "0.0###", primary: true);
        foreach (var control in new[] { frequency, gain, q }) TrackEditGesture(control);
        Grid.SetColumn(gain, 1); Grid.SetColumn(q, 2);
        details.Children.Add(frequency); details.Children.Add(gain); details.Children.Add(q);
        void CancelBandEntries()
        {
            frequency.CancelPendingEntry(); gain.CancelPendingEntry(); q.CancelPendingEntry();
        }
        void Update(GraphNode n)
        {
            int nextSelected = n.EqBands.Count == 0 || selected < 0 ? -1 : Math.Min(selected, n.EqBands.Count - 1);
            // Band removal can give the same index to a different band, whose
            // gain and Q may even equal the previous target's values.
            if (nextSelected != selected || bandCount != n.EqBands.Count) CancelBandEntries();
            selected = nextSelected; bandCount = n.EqBands.Count; updating = true;
            try
            {
                bool hasBand = selected >= 0;
                details.IsEnabled = hasBand;
                if (hasBand)
                {
                    var band = n.EqBands[selected];
                    frequency.SetDefaultValue(band.ResetFrequencyHz > 0 ? band.ResetFrequencyHz : band.FrequencyHz);
                    q.SetDefaultValue(EqBand.DefaultQForType(band.Type));
                    frequency.SetValue(band.FrequencyHz); gain.SetValue(band.GainDb); gain.IsEnabled = band.Type is not "lowPass" and not "highPass" and not "notch"; q.SetValue(band.Q);
                }
                plot.Apply(n.EqBands, n.Bypass, selected);
            }
            finally { updating = false; }
        }
        plot.SelectedBandChanged += index =>
        {
            if (updating) return;
            if (selected != index) CancelBandEntries();
            selected = index; Update(_node);
        };
        plot.BandsEdited += bands =>
        {
            if (updating || _applying) return;
            var copy = bands.Select(b => new EqBand { Type = b.Type, Enabled = b.Enabled, FrequencyHz = b.FrequencyHz, GainDb = b.GainDb, Q = b.Q, ResetFrequencyHz = b.ResetFrequencyHz }).ToList();
            _owner.NodeChanged(Id, n => n.EqBands = copy);
        };
        _effectUpdates.Add(Update);
    }

    private void TrackEditGesture(RotaryValueControl control)
    {
        control.EditGestureStarted += BeginControlEditGesture;
        control.EditGestureEnded += EndControlEditGesture;
    }
    private void BeginControlEditGesture(object? sender, EventArgs args) => _owner.Mixer.BeginEditGesture();
    private void EndControlEditGesture(object? sender, EventArgs args) => _owner.Mixer.EndEditGesture();

    private sealed class NodeSelectionOutline : FrameworkElement
    {
        internal static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(NodeSelectionOutline),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
        internal Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        private double _strokeThickness = 2;
        internal double StrokeThickness
        {
            get => _strokeThickness;
            set { if (_strokeThickness == value) return; _strokeThickness = value; InvalidateVisual(); }
        }
        protected override void OnRender(DrawingContext dc)
        {
            // The overlay fills the inner grid; extend to the unchanged outer
            // border and draw above the body but below sockets. No layout inset.
            var pen = new Pen(Stroke, _strokeThickness); pen.Freeze();
            dc.DrawRoundedRectangle(null, pen, new Rect(-1, -1, ActualWidth + 2, ActualHeight + 2), 5, 5);
        }
    }

    private sealed class AvailabilityWarningGlow : FrameworkElement
    {
        internal const double ScreenRadius = 10;
        internal static readonly DependencyProperty GlowBrushProperty = DependencyProperty.Register(nameof(GlowBrush), typeof(Brush), typeof(AvailabilityWarningGlow),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
        internal Brush GlowBrush { get => (Brush)GetValue(GlowBrushProperty); set => SetValue(GlowBrushProperty, value); }
        private double _viewScale = 1;
        internal double ViewScale
        {
            get => _viewScale;
            set { if (_viewScale == value) return; _viewScale = value; InvalidateVisual(); }
        }
        protected override void OnRender(DrawingContext dc)
        {
            // Faint concentric strokes stay entirely outside the normal node
            // border. They do not rasterize text, alter layout or intercept input.
            double step = .5 / _viewScale;
            for (double screenOffset = ScreenRadius - .25; screenOffset > 0; screenOffset -= .5)
            {
                double distance = screenOffset / _viewScale;
                var brush = GlowBrush.CloneCurrentValue();
                brush.Opacity = .35 * Math.Pow(1 - screenOffset / ScreenRadius, 2); brush.Freeze();
                var pen = new Pen(brush, step); pen.Freeze();
                dc.DrawRoundedRectangle(null, pen,
                    new Rect(-1 - distance, -1 - distance, ActualWidth + 2 + distance * 2, ActualHeight + 2 + distance * 2),
                    5 + distance, 5 + distance);
            }
        }
    }

    private sealed class ReductionStrip : FrameworkElement
    {
        private double _amount;
        internal double Amount { get => _amount; set { if (_amount == value) return; _amount = value; InvalidateVisual(); } }
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(ConsoleTheme.Get("ConsoleBorderBrush"), null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_amount > 0) dc.DrawRectangle(ConsoleTheme.Get("ConsoleABrush"), null, new Rect(0, 0, ActualWidth * _amount, ActualHeight));
        }
    }

    internal sealed class NodeMeter : StereoNodeMeter { }
}
