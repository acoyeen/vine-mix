using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>
/// Renders detached WPF content, without showing a Window or creating a native
/// window handle. Uses only in-memory preview fixtures. This path is separate
/// from --verify-ui, whose existing harness deliberately shows test windows.
/// </summary>
internal sealed class OffscreenDesignRenderer
{
    private const int Width = 1280, Height = 800;
    private readonly string _directory;
    private readonly List<string> _checks = [];
    private readonly List<string> _images = [];
    private readonly List<object> _imageSizes = [];
    private readonly List<object> _dpiEvidence = [];
    private readonly List<object> _socketLayouts = [];
    private readonly List<object> _zoomEvidence = [];
    private readonly List<object> _windowLayouts = [];
    private readonly BindingListener _bindings = new();

    public OffscreenDesignRenderer(string[] args)
    {
        int at = Array.FindIndex(args, arg => arg.Equals("--verification-dir", StringComparison.OrdinalIgnoreCase));
        if (at < 0 || at + 1 >= args.Length || string.IsNullOrWhiteSpace(args[at + 1]))
            throw new ArgumentException("--render-design requires --verification-dir followed by a new directory.");
        _directory = Path.GetFullPath(args[at + 1]);
        if (Directory.Exists(_directory) || File.Exists(_directory))
            throw new IOException("Use a new design-render directory; existing results will not be overwritten.");
        Directory.CreateDirectory(_directory);
    }

    private static FrameworkElement ClipFadePreview()
    {
        var stages = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16) };
        foreach (var stage in new[] { ("Clipping detected", 0d), ("After 2.5 seconds", 2.5d), ("After 3 seconds", 3d) })
        {
            var column = new StackPanel { Width = 150 };
            var label = new TextBlock { Text = stage.Item1, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
            var meter = new StereoNodeMeter { Height = 110, HorizontalAlignment = HorizontalAlignment.Center };
            meter.Update(0, 0, 2, true, false, false, 0);
            meter.Advance(stage.Item2);
            column.Children.Add(label); column.Children.Add(meter); stages.Children.Add(column);
        }
        var frame = new Border { Width = 482, Height = 180, Child = stages };
        frame.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        return frame;
    }

    public async Task RunBetaDocsAsync()
    {
        SourceLevels previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_bindings);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        using var mixer = new MixerViewModel(preview: true);
        GraphWorkspace? workspace = null;
        try
        {
            Require(mixer.IsPreview, "Documentation uses an inert preview, without an engine, control pipe or settings reads.");
            mixer.ApplyPreviewSnapshot(UiPreview.CreateBetaSnapshot());
            var window = new MainWindow(mixer, _ => { });
            var content = (FrameworkElement)window.Content;
            window.Content = null;
            var root = new Border { Width = Width, Height = Height, Background = window.Background,
                DataContext = mixer, Child = content, UseLayoutRounding = true, SnapsToDevicePixels = true };
            TextElement.SetFontFamily(root, window.FontFamily); TextElement.SetFontSize(root, window.FontSize);
            root.SetResourceReference(TextElement.ForegroundProperty, "ConsoleTextBrush");
            root.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
            window.Content = root;
            workspace = window.Workspace;
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, workspace));
            foreach (bool configured in new[] { false, true })
            {
                var fixture = UiPreview.CreateBetaSnapshot(configured);
                Require(fixture.Configuration.Graph.Nodes.Count == 3 && fixture.Configuration.Graph.Edges.Count == 2,
                    "The beta screenshot has exactly physical input, virtual output, listening output and two routes.");
                Require(fixture.Configuration.Graph.Nodes.Where(node => node.Kind is "output" or "virtualOutput").All(node => node.Muted),
                    "Both starter outputs remain muted in fresh and configured examples.");
                Require(configured || fixture.Configuration.Graph.Nodes.All(node => string.IsNullOrEmpty(node.Device.Id)),
                    "Fresh documentation has no selected physical or virtual device.");
                Require(fixture.Endpoints.All(endpoint => endpoint.Id.StartsWith("preview-example-", StringComparison.Ordinal)),
                    "Every configured documentation endpoint is explicitly synthetic.");
                mixer.ApplyPreviewSnapshot(fixture);
                await Layout(root); workspace.FitAll(); await Layout(root); workspace.UpdateWires(); await Layout(root);
                Require(Descendants<GraphNodeView>(root).Count() == 3, "The real workspace renders exactly three starter node controls.");
                var outputViews = Descendants<GraphNodeView>(root).ToDictionary(node => node.Id);
                Rect virtualBounds = outputViews["starter-virtual-output"].TransformToAncestor(root)
                    .TransformBounds(new Rect(new Point(), outputViews["starter-virtual-output"].RenderSize));
                Rect listeningBounds = outputViews["starter-listening-output"].TransformToAncestor(root)
                    .TransformBounds(new Rect(new Point(), outputViews["starter-listening-output"].RenderSize));
                Require(virtualBounds.Bottom < listeningBounds.Top,
                    "Both output node bodies are fully visible with a positive gap at the required starter positions.");
                Require(window.BetaVersionLabel.Text == ReleaseInfo.DisplayVersion && window.BetaVersionLabel.ActualWidth > 0,
                    "The caption displays the packaged beta version beside the engine indicator.");
                Render(root, configured ? "workspace.png" : "first-run.png");
                Render(root, configured ? "workspace-192dpi.png" : "first-run-192dpi.png", 192);
            }
            RenderMenu(MainWindow.CreateSettingsMenu(mixer), "settings-menu.png");
            string? copiedVersion = null;
            bool aboutClosed = false;
            var about = MainWindow.CreateAboutContent(() => aboutClosed = true, value => copiedVersion = value);
            var aboutHost = new Border { Width = 520, Child = about, UseLayoutRounding = true, SnapsToDevicePixels = true };
            TextElement.SetFontFamily(aboutHost, window.FontFamily); TextElement.SetFontSize(aboutHost, 13);
            aboutHost.SetResourceReference(TextElement.ForegroundProperty, "ConsoleTextBrush");
            aboutHost.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
            aboutHost.Measure(new Size(520, double.PositiveInfinity)); aboutHost.Height = Math.Ceiling(aboutHost.DesiredSize.Height);
            await Layout(aboutHost);
            var versionText = Descendants<TextBox>(aboutHost).Single();
            Require(versionText.IsReadOnly && versionText.Text == ReleaseInfo.FullInformation,
                "About exposes selectable full version, build, source and distribution information.");
            Render(aboutHost, "about.png");
            var aboutButtons = Descendants<Button>(aboutHost).ToArray();
            aboutButtons.Single(button => Equals(button.Content, "Copy version information")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(copiedVersion == ReleaseInfo.FullInformation,
                "The real About Copy action passes complete version information to an injected inert clipboard action.");
            aboutButtons.Single(button => Equals(button.Content, "Close")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(aboutClosed, "The real About Close action invokes its close callback.");
            RequireNoNativeWindow(root);
            Require(_bindings.Messages.Length == 0, "Sanitized documentation renders have no WPF binding errors.");
            WriteReport(true, null);
        }
        finally
        {
            workspace?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, workspace));
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindings);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }
    }

    public async Task RunAsync()
    {
        SourceLevels previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_bindings);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        using var mixer = new MixerViewModel(preview: true);
        GraphWorkspace? workspace = null;
        try
        {
            Require(mixer.IsPreview, "Only the in-memory preview model is constructed; no engine connection or supervisor is created.");
            CheckNumericAdjustment();
            CheckRotaryAdjustment();
            CheckEditDispatchCadence();
            CheckConfigurationRefreshDecisions();
            var copyPerformance = ModelMaintenanceVerification.Run(Require);
            File.WriteAllText(Path.Combine(_directory, "model-copy-performance.json"), JsonSerializer.Serialize(copyPerformance, new JsonSerializerOptions { WriteIndented = true }));
            await EngineConnectionVerification.RunAsync(Require);
            EngineLaunchVerification.Run(Require);
            StandaloneLaunchVerification.Run(Require);
            BetaPreparationVerification.Run(Require);
            await BetaRecoveryVerification.RunAsync(Require);
            await BetaEngineIdentityVerification.RunAsync(Require);
            await EngineRestartVerification.RunAsync(Require);
            await SettingsRestartVerification.RunAsync(Require);
            await UpdatePreparationVerification.RunAsync(Require);
            await UpdateUiVerification.RunAsync(Require);
            await AddNodeHelpVerification.RunAsync(Require);
            DeviceVisibilitySettingsVerification.Run(Require);
            NodeSearchVerification.Run(Require);
            GraphNodeAlignmentVerification.Run(Require);
            Render(DeviceChoicesVerification.Run(Require), "device-visibility.png");
            GraphConnectionVerification.Run(Require);
            WorkspaceHistoryVerification.Run(Require);
            GestureHistoryVerification.Run(Require);
            mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot(["--signal"]));

            // Construct the real main layout and wrap its content. Never call
            // Show, Activate, Focus, EnsureHandle, or Window.Measure/Arrange.
            // NativeCaption registers callbacks, but no SourceInitialized event
            // can occur while the window has no HWND.
            var openedWebsites = new List<string>();
            var window = new MainWindow(mixer, openedWebsites.Add);
            Render(RotaryControlVerification.Run(Require), "rotary-controls.png");
            Render(ToolTipVerification.Run(Require), "tooltips.png");
            CheckNumericResetAndEntryTemplates();
            CheckStereoMeters();
            PeakMeterVerification.Run(Require);
            Render(ClipFadePreview(), "clip-fade-stages.png");
            var meterZones = NodeRefinementVerification.CheckMeterZones(Require);
            Render(meterZones, "meter-colour-zones.png");
            Require(window.Width == Width && window.Height == Height && window.MinWidth == 900 && window.MinHeight == 580
                && double.IsPositiveInfinity(window.MaxWidth) && double.IsPositiveInfinity(window.MaxHeight)
                && window.ResizeMode == ResizeMode.CanResize,
                "The main window starts at 1280 x 800, supports resizing from 900 x 580 and has no fixed maximum dimensions.");
            Require(window.Content is FrameworkElement, "The actual MainWindow content is available for detached rendering.");
            var content = (FrameworkElement)window.Content;
            window.Content = null;
            workspace = window.Workspace;
            var root = new Border
            {
                Width = Width, Height = Height, Background = window.Background,
                DataContext = mixer, Child = content,
                UseLayoutRounding = true, SnapsToDevicePixels = true
            };
            TextElement.SetFontFamily(root, window.FontFamily);
            Require(new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var interfaceTypeface)
                && interfaceTypeface.FontUri.ToString().Contains("IBM-Plex-Sans-Regular", StringComparison.OrdinalIgnoreCase),
                "The main interface resolves the bundled IBM Plex Sans font rather than silently falling back to a system font.");
            TextElement.SetFontSize(root, window.FontSize);
            root.SetResourceReference(TextElement.ForegroundProperty, "ConsoleTextBrush");
            root.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
            // Keep the invisible Window as a logical resource owner so dynamic
            // application resources invalidate throughout the render tree. The
            // tree still has no native PresentationSource: only this Border is
            // measured, arranged and rendered, and the Window never gets an HWND.
            window.Content = root;
            RequireNoNativeWindow(root);
            // Keep the fixture baseline independent of the user's current
            // monitor scale; subsequent captures explicitly exercise each DPI.
            VisualTreeHelper.SetRootDpi(VisualRoot(root), new DpiScale(1, 1));

            // Loaded initializes only the existing canvas's in-memory views and
            // subscriptions. Raising this routed event is not a desktop action;
            // the detached visual never receives a PresentationSource.
            workspace.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, workspace));
            await Layout(root);
            workspace.FitAll(); await Layout(root); workspace.UpdateWires(); await Layout(root);
            Require(Descendants<Button>(root).Any(button => AutomationProperties.GetName(button) == "Maximise window"),
                "The integrated caption exposes an accessible Maximize action; no native window state is changed by this detached check.");
            Require(Descendants<GraphNodeView>(root).Count() == 8, "The detached main workspace lays out all eight migrated fixture nodes.");
            Require(mixer.GraphConfig.Edges.Count == 9 && GraphDefaults.Validate(mixer.GraphConfig).Length == 0,
                "The migrated preview graph retains nine structurally valid connections.");
            await MenuHoverHandoffVerification.RunAsync(window, mixer, root, () => Layout(root), Require);
            await OrdinaryHoverHelpVerification.RunAsync(window, mixer, root, () => Layout(root), Require);
            await BrandingVerification.RunAsync(window, root, () => Layout(root), Require);
            window.WebsiteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(openedWebsites.SequenceEqual(new[] { "https://vinemix.app" }),
                "Activating the actual caption button opens exactly the public VineMix website through the injected browser launcher.");
            Render(root, "workspace.png");
            Render(root, "workspace-144dpi.png", 144);
            Render(root, "workspace-192dpi.png", 192);
            string[] headerKeys = ["NodeInputHeaderBrush", "NodeOutputHeaderBrush", "NodeProcessorHeaderBrush", "NodeUtilityHeaderBrush", "NodeControllerHeaderBrush"];
            foreach (string key in headerKeys)
                Require(ConsoleTheme.Get(key) is SolidColorBrush header && (header.Color.R != header.Color.G || header.Color.G != header.Color.B),
                    key + " restores a coloured category header while node text retains grayscale antialiasing.");
            Require(headerKeys.Select(key => ((SolidColorBrush)ConsoleTheme.Get(key)).Color).Distinct().Count() == headerKeys.Length,
                "Input, output, processor, utility and controller categories each use a distinct header colour.");
            Require(new[] { "ConsoleWindowBrush", "ConsolePanelBrush", "NodeCanvasBrush" }.All(key => ConsoleTheme.Get(key) is SolidColorBrush brush
                && brush.Color.R <= 32 && brush.Color.G <= 32 && brush.Color.B <= 32),
                "Window, canvas and node body backgrounds use near-black values while controls and text remain independently contrasted.");
            await CheckFractionalZoomAndWindowLayouts(root, workspace, mixer);
            await CheckCanvasRetention(root, workspace, mixer);
            await CheckDropdownWheelRouting(root, workspace, mixer);
            Render(EqEditorVerification.Run(Require), "eq-editor-spectrum.png");
            EqTextLayoutVerification.Run(Require, (element, name, dpi) => Render(element, name, dpi));
            Render(EqBandSelectionVerification.Run(Require), "eq-band-multiselection.png");
            Render(EqBandSelectionVerification.MarqueePreview(), "eq-band-box-selection.png");
            await EqDefaultsVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name));
            await NodeDefaultsVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await NumericTabCommitVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await DenoiserSelectionVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name));
            await SocketPresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => {
                    RenderNode(view, workspace, name);
                    RenderNode(view, workspace, name.Replace(".png", "-144dpi.png"), 144);
                    RenderNode(view, workspace, name.Replace(".png", "-192dpi.png"), 192);
                });
            await DynamicsFeedbackVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name));
            await GraphGroupVerification.RunAsync(workspace, mixer, () => Layout(root), Require, name => Render(root, name));
            workspace.SetMessage("");
            await CheckMicrophoneControls(root, workspace, mixer);
            await NodePresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await PeakMeterPresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (element, name) => Render(element, name));
            InputAvailabilityVerification.Run(Require);
            await InputAvailabilityPresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name,
                    availabilityPulseSeconds: name.Contains("pulse-normal", StringComparison.Ordinal) ? .8 : 0));
            OutputAvailabilityVerification.Run(Require);
            await OutputAvailabilityPresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name,
                    availabilityPulseSeconds: name.Contains("pulse-normal", StringComparison.Ordinal) ? .8 : 0));
            VirtualDeviceBindingVerification.Run(Require);
            await VirtualDevicePresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name));
            await VoiceColorVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name));
            await CheckVoiceColorRendering(root, workspace, mixer);
            await CoreDynamicsVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (node, name) => RenderNode(node, workspace, name));
            await DynamicsOrganizationVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (node, name) => RenderNode(node, workspace, name));
            await NodeDialRedesignVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (node, name) => {
                    RenderNode(node, workspace, name);
                    RenderNode(node, workspace, name.Replace(".png", "-144dpi.png"), 144);
                    RenderNode(node, workspace, name.Replace(".png", "-192dpi.png"), 192);
                }, name => {
                    Render(root, name);
                    double oldWidth = root.Width, oldHeight = root.Height;
                    try
                    {
                        foreach (var size in new[] { (Width: 1920, Height: 1080), (Width: 900, Height: 580) })
                        {
                            root.Width = size.Width; root.Height = size.Height;
                            MeasureAtCurrentDpi(root, root.Width, root.Height);
                            workspace.FitAll(); MeasureAtCurrentDpi(root, root.Width, root.Height); workspace.UpdateWires();
                            foreach (double dpi in new[] { 96d, 144d, 192d })
                                Render(root, $"dynamics-populated-{size.Width}x{size.Height}-{dpi:0}dpi.png", dpi);
                        }
                    }
                    finally { root.Width = oldWidth; root.Height = oldHeight; MeasureAtCurrentDpi(root, oldWidth, oldHeight); }
                });
            await WorkspaceChromeVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await WorkspaceFeedbackVerification.RunAsync(root, workspace, mixer, () => Layout(root), Require, name => Render(root, name));
            await DuplicateInputVerification.RunAsync(workspace, mixer, () => Layout(root), Require, name => Render(root, name));
            await PendingPasteVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await CanvasNavigationVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await FrameSelectionVerification.RunAsync(workspace, mixer, () => Layout(root), Require, name => Render(root, name));
            await WorkspaceHelpVerification.RunAsync(window, mixer, root, () => Layout(root), Require, name => Render(root, name), (name, dpi) => Render(root, name, dpi));
            await SettingsSearchVerification.RunAsync(window, mixer, root, () => Layout(root), Require, (menu, name) => RenderMenu(menu, name));
            await NodeSearchPresentationVerification.RunAsync(window, mixer, root, () => Layout(root), Require, name => Render(root, name));
            await NodeAlignmentPresentationVerification.RunAsync(workspace, mixer, () => Layout(root), Require, name => Render(root, name));
            await NodeAlignmentReplayVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await NodeAlignmentShortcutVerification.RunAsync(workspace, mixer, () => Layout(root), Require, name => Render(root, name));
            await CaptionHitTestVerification.RunAsync(root, window, workspace, mixer, () => Layout(root), Require);
            await ResizeRedrawVerification.RunAsync(root, window, workspace, mixer, () => Layout(root), Require);
            RenderMenu(workspace.CreateAddMenu(new Point(20, 20)), "add-node-menu.png");
            // Caption fixtures intentionally load a fresh session. Populate
            // Activity with explicit edits rather than relying on stale history
            // leaking across those fixture loads.
            string activityNode = mixer.GraphConfig.Nodes[0].Id;
            workspace.NodeChanged(activityNode, node => node.Name = "Microphone");
            workspace.NodeChanged(activityNode, node => node.GainDb = -18);
            workspace.SetMessage("Microphone adjusted. Use Undo to restore its previous settings.");
            await Layout(root);
            RenderMenu(workspace.CreateHistoryMenu(), "edit-history-menu.png");
            RenderMenu(MainWindow.CreateSettingsMenu(mixer), "settings-menu.png");
            await NodeRefinementVerification.RunAsync(root, workspace, mixer, () => Layout(root), Require,
                (view, name) => RenderNode(view, workspace, name));
            await CheckNewEffectsLayouts(root, workspace, mixer);
            await ConnectionDetailsVerification.RunAsync(workspace, mixer, () => Layout(root), Require,
                (element, name) => {
                    if (element is ContextMenu menu) RenderMenu(menu, name);
                    else Render(element, name);
                });
            await CableHoverBoundsVerification.RunAsync(workspace, mixer, () => Layout(root), Require);
            await GraphConnectionVerification.RunAsync(workspace, mixer, () => Layout(root), Require, name =>
            {
                // Include both endpoints and the target highlight without panning or
                // changing the fixture's world coordinates during its gesture.
                double width = root.Width, height = root.Height;
                try { root.Width = 1680; root.Height = 1100; Render(root, name + ".png"); }
                finally { root.Width = width; root.Height = height; MeasureAtCurrentDpi(root, width, height); }
            });
            workspace.SetMessage("");

            var effects = UiPreview.CreateSnapshot([]);
            var eq = GraphDefaults.NewNode("eq", 30, 35);
            eq.EqBands = EqBand.Defaults(); // Existing configured effects and band-interaction fixture.
            eq.EqBands[0].GainDb = 3;
            eq.EqBands[2].FrequencyHz = 1800; eq.EqBands[2].GainDb = -6; eq.EqBands[2].Q = 1.4;
            eq.EqBands[4].GainDb = 2;
            var compressor = GraphDefaults.NewNode("compressor", 1090, 35);
            compressor.Compressor.ThresholdDb = -22; compressor.Compressor.Ratio = 3;
            var tone = GraphDefaults.NewNode("tone", 680, 35);
            tone.EqBands[0].GainDb = 3; tone.EqBands[1].GainDb = -2; tone.EqBands[2].GainDb = 1;
            effects.Configuration.Graph = new() { Nodes = [eq, compressor, tone], Zoom = 1, PanX = 20, PanY = 20 };
            effects.Status.Nodes = effects.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
                { Id = node.Id, Peak = .12f, PeakLeft = .12f, PeakRight = .04f, Channels = 2, Path = new() { State = "Running" } }).ToList();
            mixer.ApplyPreviewSnapshot(effects);
            await Layout(root); workspace.FitAll(); await Layout(root); workspace.UpdateWires(); await Layout(root);
            Require(Descendants<GraphNodeView>(root).Count() == 3 && mixer.GraphConfig.Nodes.Single(node => node.Kind == "eq").EqBands.Count == 5,
                "The effects fixture lays out a graphical EQ, compressor and three tone dials inside three actual node views.");
            Render(root, "effects.png");
            Render(root, "effects-144dpi.png", 144);
            Render(root, "effects-192dpi.png", 192);
            double effectsZoom = mixer.GraphConfig.Zoom;
            mixer.Edit(c => c.Graph.Zoom = 1); await Layout(root); workspace.UpdateWires(); await Layout(root);
            var effectsLayouts = CaptureNodeLayouts(root);
            foreach (double zoom in new[] { .3, .5, .75, .9, 1.1, 1.5, 2d })
            {
                mixer.Edit(c => c.Graph.Zoom = zoom); await Layout(root); workspace.UpdateWires(); await Layout(root);
                CheckProportionalNodeLayouts(root, workspace, effectsLayouts, zoom, "effects");
                Require(Descendants<EqualizerEditor>(root).All(editor => editor.ActualWidth > 0 && editor.ActualHeight > 0 && EffectivelyVisible(editor, root))
                    && Descendants<RotaryGainControl>(root).Count(dial => EffectivelyVisible(dial, root)) == 3,
                    $"At {zoom:P0}, the effects canvas retains its visible actual EQ plot and all three dials; zoom does not hide editors.");
                if (zoom <= 1.1)
                {
                    Render(root, $"effects-{zoom * 100:0}percent.png");
                    Render(root, $"effects-{zoom * 100:0}percent-144dpi.png", 144);
                }
            }
            mixer.Edit(c => c.Graph.Zoom = effectsZoom); await Layout(root); workspace.UpdateWires(); await Layout(root);

            var eqView = Descendants<GraphNodeView>(root).Single(node => node.Id == eq.Id);
            var toneView = Descendants<GraphNodeView>(root).Single(node => node.Id == tone.Id);
            var retainedEq = Descendants<EqualizerEditor>(eqView).Single();
            var retainedDials = Descendants<RotaryGainControl>(toneView).ToArray();
            int eqRefreshes = retainedEq.SettingsRefreshCount;
            int dialRefreshes = retainedDials.Sum(dial => dial.FaceRefreshCount);
            for (int tick = 0; tick < 60; tick++) mixer.RefreshPreviewTelemetry();
            Require(retainedEq.SettingsRefreshCount == eqRefreshes && retainedDials.Sum(dial => dial.FaceRefreshCount) == dialRefreshes,
                "Sixty unchanged telemetry refreshes retain the EQ response and dial drawings rather than recomputing them at meter frequency.");
            Require(Descendants<RotaryGainControl>(toneView).Count() == 3 && GraphDefaults.Validate(mixer.GraphConfig).Length == 0,
                "Low / Mid / High contains three real rotary controls and valid fixed-filter settings.");
            RenderNode(toneView, workspace, "tone-closeup.png");
            RenderNode(toneView, workspace, "tone-closeup-192dpi.png", 192);
            CheckToneEdits(toneView, mixer);
            CheckEqualizerEdits(eqView, mixer);
            await Layout(root);
            string beforeSelection = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
            workspace.SelectNode(eq.Id); await Layout(root);
            Require(eqView.SelectionOutlineVisible
                && Descendants<GraphNodeView>(root).All(node => ReferenceEquals(node.BorderBrush, ConsoleTheme.Get("NodeOutlineBrush")))
                && Descendants<GraphNodeView>(root).Where(node => node.Id != eq.Id).All(node => !node.SelectionOutlineVisible)
                && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == beforeSelection,
                "Programmatic in-memory selection highlights only EQ and does not change graph settings; pointer selection is not exercised.");
            CheckSockets(eqView, collapsed: false);
            Render(root, "effects-selected.png");
            await CheckSelectionOutlines(root, workspace, mixer, eq.Id, tone.Id);

            Require(!Descendants<Expander>(eqView).Any() && !Descendants<ComboBox>(eqView).Any(),
                "Equalizer values are directly visible, with no Precise values expander or filter-type dropdown.");
            var gainSlider = Descendants<Slider>(eqView).Single(slider => AutomationProperties.GetName(slider) == "Band gain");
            var expectedGraph = mixer.GraphConfig.Copy();
            var expectedEq = expectedGraph.Nodes.Single(node => node.Id == eq.Id);
            double originalBandGain = expectedEq.EqBands[0].GainDb;
            expectedEq.EqBands[0].GainDb = 7.5;
            // The first EQ band is selected by the real node's initialization.
            // Change its actual Slider.Value without focus, mouse or keyboard.
            gainSlider.Value = 7.5;
            await Layout(root);
            Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expectedGraph, Protocol.Json),
                "Programmatically changing the dial's accessible Band gain range updates only selected EQ band 1 to +7.5 dB; all other bands, nodes, routes and canvas settings remain unchanged.");
            gainSlider.Value = originalBandGain;
            await Layout(root);
            Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == beforeSelection,
                "Restoring Band gain through the same in-memory dial restores the complete effects fixture.");
            RenderNode(eqView, workspace, "eq-closeup.png");
            RenderNode(eqView, workspace, "eq-closeup-144dpi.png", 144);
            RenderNode(eqView, workspace, "eq-closeup-192dpi.png", 192);

            double expandedHeight = eqView.ActualHeight;
            workspace.NodeChanged(eq.Id, node => node.Collapsed = true);
            await Layout(root); workspace.UpdateWires(); await Layout(root);
            Require(Near(eqView.ActualHeight, expandedHeight)
                && mixer.GraphConfig.Nodes.Single(node => node.Id == eq.Id).Collapsed
                && !Descendants<Button>(eqView).Any(button => AutomationProperties.GetName(button) is "Expand node" or "Collapse node")
                && EffectivelyVisible(gainSlider, eqView),
                "A legacy collapsed flag preserves every visible EQ control and the same dimensions, without a collapse button.");
            CheckSockets(eqView, collapsed: false);
            Render(root, "effects-legacy-expanded.png");
            workspace.NodeChanged(eq.Id, node => node.Collapsed = false);
            await Layout(root); workspace.UpdateWires(); await Layout(root);
            Require(Math.Abs(eqView.ActualHeight - expandedHeight) < .01 && EffectivelyVisible(gainSlider, eqView)
                && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == beforeSelection,
                "Clearing the inert legacy flag leaves dimensions and controls unchanged and restores the original fixture settings.");

            ConsoleTheme.SetPreviewHighContrast(true); await Layout(root);
            var gainEntry = Descendants<TextBox>(eqView).Single(entry => AutomationProperties.GetName(entry) == "Band gain value");
            var gainFace = Descendants<Border>(gainEntry).Single(border => ReferenceEquals(border.TemplatedParent, gainEntry));
            Require(SameColour(root.Background, SystemColors.WindowBrush)
                && SameColour(eqView.Background, SystemColors.WindowBrush)
                && SameColour(gainFace?.Background, SystemColors.WindowBrush)
                && SameColour(TextElement.GetForeground(root), SystemColors.WindowTextBrush),
                "High-contrast resource updates reach the offscreen root, EQ body, rotary numeric-entry template and inherited text; screenshot review is still required.");
            RenderNode(eqView, workspace, "eq-high-contrast-closeup.png");
            workspace.FitAll(); await Layout(root); workspace.UpdateWires(); await Layout(root);
            Render(root, "effects-high-contrast.png");
            ConsoleTheme.SetPreviewHighContrast(null); await Layout(root);

            mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot(["--waiting"]));
            await Layout(root); workspace.FitAll(); await Layout(root); workspace.UpdateWires(); await Layout(root);
            Require(mixer.GraphStatus.Any(node => node.Path.State == "Waiting"), "The waiting-device fixture supplies disconnected states without querying hardware.");
            Render(root, "waiting.png");
            await CheckFitAtDifferentZooms(root, workspace, mixer);

            var empty = UiPreview.CreateSnapshot([]); empty.Configuration.Graph = new(); empty.Status.Nodes = [];
            mixer.ApplyPreviewSnapshot(empty); await Layout(root);
            Require(Descendants<GraphNodeView>(root).Count() == 0, "The empty fixture removes prior node views and presents the canvas empty state.");
            Render(root, "empty.png");

            RequireNoNativeWindow(root);
            Require(_bindings.Messages.Length == 0, "No WPF binding errors occurred while constructing and rendering these detached layouts.");
            WriteReport(true, null);
        }
        finally
        {
            workspace?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, workspace));
            ConsoleTheme.SetPreviewHighContrast(null);
            PresentationTraceSources.DataBindingSource.Listeners.Remove(_bindings);
            PresentationTraceSources.DataBindingSource.Switch.Level = previousLevel;
        }
    }

    private async Task CheckVoiceColorRendering(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        var input = GraphDefaults.NewNode("microphone", 24, 30);
        var color = GraphDefaults.NewNode("voiceColor", 450, 30);
        var output = GraphDefaults.NewNode("output", 900, 30);
        color.VoiceColor.Tone = .32; color.VoiceColor.Ambience = .6;
        fixture.Configuration.Graph = new() { Nodes = [input, color, output], Zoom = .75, PanX = 20, PanY = 20,
            Edges = [new() { Id = "voice-preview-in", From = input.Id, To = color.Id },
                new() { Id = "voice-preview-out", From = color.Id, To = output.Id }] };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Channels = 2, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await Layout(root); workspace.UpdateWires(); await Layout(root);
        Render(root, "voice-color-workspace-75percent.png");
        Render(root, "voice-color-workspace-75percent-144dpi.png", 144);
        var view = Descendants<GraphNodeView>(root).Single(node => node.Id == color.Id);
        RenderNode(view, workspace, "voice-color-closeup.png");
        RenderNode(view, workspace, "voice-color-closeup-144dpi.png", 144);
        RenderNode(view, workspace, "voice-color-closeup-192dpi.png", 192);
        foreach (var position in new[] { (Name: "neutral", Tone: 0d, Character: 0d),
            (Name: "warm-natural", Tone: -1d, Character: 0d),
            (Name: "bright-natural", Tone: 1d, Character: 0d),
            (Name: "warm-broadcast", Tone: -1d, Character: 1d),
            (Name: "bright-broadcast", Tone: 1d, Character: 1d),
            (Name: "broadcast", Tone: 0d, Character: 1d) })
        {
            workspace.NodeChanged(color.Id, node => { node.VoiceColor.Tone = position.Tone; node.VoiceColor.Ambience = position.Character; });
            await Layout(root);
            RenderNode(view, workspace, $"voice-color-pad-{position.Name}.png");
        }
        workspace.NodeChanged(color.Id, node => { node.VoiceColor.Tone = .32; node.VoiceColor.Ambience = .6; });
        await Layout(root);
        ConsoleTheme.SetPreviewHighContrast(true); await Layout(root);
        RenderNode(view, workspace, "voice-color-high-contrast.png");
        ConsoleTheme.SetPreviewHighContrast(null); await Layout(root);
    }

    private async Task CheckCanvasRetention(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        mixer.Edit(c => { c.Graph.Zoom = 1; c.Graph.PanX = 20; c.Graph.PanY = 20; });
        await Layout(root); workspace.UpdateWires(); await Layout(root);
        long wires = workspace.WireGeometryBuilds, transforms = workspace.ViewTransformUpdates, grid = workspace.GridTileRebuilds;
        for (int tick = 0; tick < 60; tick++) { mixer.RefreshPreviewTelemetry(); await Layout(root); }
        Require(workspace.WireGeometryBuilds == wires && workspace.ViewTransformUpdates == transforms && workspace.GridTileRebuilds == grid,
            "Sixty unchanged telemetry/layout ticks reuse wire geometry, the canvas transform and grid tile.");
        var anchor = new Point(300, 300);
        for (int tick = 0; tick < 3; tick++) workspace.ApplyWheelDelta(anchor, -30);
        Require(mixer.GraphConfig.Zoom == 1, "Three quarter-notch wheel events do not each trigger a full zoom jump.");
        workspace.ApplyWheelDelta(anchor, -30); await Layout(root);
        Require(Math.Abs(mixer.GraphConfig.Zoom - .9) < .00001, "Accumulated wheel input performs exactly one zoom step at one complete notch.");
        long fractionalWires = workspace.WireGeometryBuilds;
        for (int tick = 0; tick < 20; tick++) { mixer.RefreshPreviewTelemetry(); await Layout(root); }
        Require(workspace.WireGeometryBuilds == fractionalWires,
            "At fractional zoom, unchanged telemetry retains cables and the unchanged world-space socket geometry.");
        workspace.ApplyWheelDelta(anchor, -240); await Layout(root);
        Require(Math.Abs(mixer.GraphConfig.Zoom - .6) < .00001, "A two-notch wheel event applies two stops in one view update.");
        Require(Descendants<GraphNodeView>(root).All(node => node.DetailsVisible) && workspace.WireGeometryBuilds == wires,
            "Zooming from 100% through 90% to 60% keeps all editors visible and reuses unchanged world-space cable geometry.");
        foreach (double zoom in new[] { 1d, .5, .3 })
        {
            mixer.Edit(c => c.Graph.Zoom = zoom); await Layout(root);
            Require(workspace.GridSpacing >= 16 && workspace.GridDotCountBound < 4000,
                $"The {zoom:P0} view keeps grid spacing at least 16 DIPs and density below 4,000 visible dots.");
        }
        double previousX = mixer.GraphConfig.Nodes[0].X;
        mixer.Edit(c => c.Graph.Nodes[0].X += 80); await Layout(root);
        Require(workspace.WireGeometryBuilds > wires, "Moving a node still rebuilds its connected cable anchors.");
        mixer.Edit(c => c.Graph.Nodes[0].X = previousX); await Layout(root);
    }

    private async Task CheckFractionalZoomAndWindowLayouts(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        var nodeSettings = mixer.GraphConfig.Nodes.Select(node => (node.Id, node.X, node.Y, node.Collapsed)).ToArray();
        mixer.Edit(c => { c.Graph.Zoom = 1; c.Graph.PanX = 24; c.Graph.PanY = 24; });
        await Layout(root); workspace.UpdateWires(); await Layout(root);
        var layouts = CaptureNodeLayouts(root);
        foreach (double zoom in new[] { .3, .5, .75, .9, 1.1, 1.5, 2d })
        {
            mixer.Edit(c => { c.Graph.Zoom = zoom; c.Graph.PanX = 24; c.Graph.PanY = 24; });
            await Layout(root); workspace.UpdateWires(); await Layout(root);
            CheckProportionalNodeLayouts(root, workspace, layouts, zoom, "workspace");
            Require(mixer.GraphConfig.Nodes.Select(node => (node.Id, node.X, node.Y, node.Collapsed)).SequenceEqual(nodeSettings),
                $"The {zoom:P0} zoom leaves every saved position and explicit collapse state unchanged.");
            Render(root, $"workspace-{zoom * 100:0}percent.png");
            Render(root, $"workspace-{zoom * 100:0}percent-144dpi.png", 144);
        }
        string collapsedId = mixer.GraphConfig.Nodes[0].Id;
        workspace.NodeChanged(collapsedId, node => node.Collapsed = true);
        await Layout(root);
        double collapsedHeight = Descendants<GraphNodeView>(root).Single(node => node.Id == collapsedId).ActualHeight;
        foreach (double zoom in new[] { .3, .75, 2d })
        {
            mixer.Edit(c => c.Graph.Zoom = zoom); await Layout(root);
            var nodes = Descendants<GraphNodeView>(root).ToArray();
            var collapsed = nodes.Single(node => node.Id == collapsedId);
            Require(collapsed.DetailsVisible && Math.Abs(collapsed.ActualHeight - collapsedHeight) < .00001
                && nodes.Where(node => node.Id != collapsedId).All(node => node.DetailsVisible)
                && mixer.GraphConfig.Nodes.Single(node => node.Id == collapsedId).Collapsed,
                $"At {zoom:P0}, legacy collapsed flags are preserved in data while every node keeps its complete visible controls and constant world-space dimensions.");
        }
        workspace.NodeChanged(collapsedId, node => node.Collapsed = false);
        mixer.Edit(c => { c.Graph.Zoom = 1; c.Graph.PanX = 24; c.Graph.PanY = 24; });
        await Layout(root);
        CheckProportionalNodeLayouts(root, workspace, layouts, 1, "restored workspace");
        foreach (var size in new[] { new Size(900, 580), new Size(1280, 800), new Size(1920, 1080) })
        {
            root.Width = size.Width; root.Height = size.Height;
            await Layout(root);
            var caption = Descendants<Button>(root).Single(button => AutomationProperties.GetName(button) == "Maximise window");
            var bounds = caption.TransformToAncestor(root).TransformBounds(new Rect(new Point(), caption.RenderSize));
            Require(workspace.ActualWidth > 0 && workspace.ActualHeight > 0 && bounds.Left >= 0 && bounds.Top >= 0
                && bounds.Right <= root.ActualWidth && bounds.Bottom <= root.ActualHeight,
                $"The {size.Width:0} x {size.Height:0} layout retains a positive canvas and an unclipped maximise caption action.");
            Require(mixer.GraphConfig.Zoom == 1 && mixer.GraphConfig.Nodes.Select(node => (node.Id, node.X, node.Y, node.Collapsed)).SequenceEqual(nodeSettings),
                $"Resizing the detached content to {size.Width:0} x {size.Height:0} does not reposition nodes, change saved zoom or change collapse states.");
            _windowLayouts.Add(new { width = size.Width, height = size.Height, canvasWidth = workspace.ActualWidth,
                canvasHeight = workspace.ActualHeight, captionBounds = new { bounds.X, bounds.Y, bounds.Width, bounds.Height } });
            Render(root, $"workspace-window-{size.Width:0}x{size.Height:0}.png");
        }
        root.Width = Width; root.Height = Height; await Layout(root);
    }

    private async Task CheckDropdownWheelRouting(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        CheckClosedDropdownWheel();
        var owner = new ComboBox();
        var popupLeaf = new Border();
        var popup = new Popup { PlacementTarget = owner, Child = new Border { Child = popupLeaf } };
        Require(!popup.IsOpen && PresentationSource.FromVisual(popupLeaf) is null
            && GraphWorkspace.IsWheelPopupSource(popupLeaf) && GraphWorkspace.IsWheelPopupSource(popup),
            "An unopened, detached dropdown popup and its separate child tree yield wheel input to the dropdown without creating a native window.");

        var run = new Run("Detached item");
        var label = new TextBlock(run);
        var item = new ComboBoxItem { Content = label }; owner.Items.Add(item);
        Require(GraphWorkspace.IsWheelPopupSource(item) && GraphWorkspace.IsWheelPopupSource(label)
            && GraphWorkspace.IsWheelPopupSource(run),
            "Dropdown item, text and content-element sources remain protected through logical/content parent relationships.");

        var templatedOwner = new ComboBox
        {
            Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ComboBox}">
                  <Grid>
                    <Border/>
                    <Popup x:Name="PART_Popup" PlacementTarget="{Binding RelativeSource={RelativeSource TemplatedParent}}">
                      <Border x:Name="DetachedWheelSurface"/>
                    </Popup>
                  </Grid>
                </ControlTemplate>
                """)
        };
        templatedOwner.ApplyTemplate();
        var templatedSurface = templatedOwner.Template.FindName("DetachedWheelSurface", templatedOwner) as Border
            ?? throw new InvalidOperationException("The detached ComboBox template did not create its wheel-test surface.");
        var templatedPopup = templatedOwner.Template.FindName("PART_Popup", templatedOwner) as Popup
            ?? throw new InvalidOperationException("The detached ComboBox template did not create its unopened popup.");
        Require(!templatedPopup.IsOpen && ReferenceEquals(templatedSurface.TemplatedParent, templatedOwner)
            && GraphWorkspace.IsWheelPopupSource(templatedSurface),
            "A real ComboBox template's unopened popup surface is protected through its templated-parent association.");
        var scrollLeaf = new Border(); var scroll = new ScrollViewer { Content = scrollLeaf };
        Require(!GraphWorkspace.IsWheelPopupSource(scrollLeaf)
            && GraphWorkspace.IsWheelPopupSource(new MenuItem())
            && GraphWorkspace.IsWheelPopupSource(new ContextMenu())
            && !GraphWorkspace.IsWheelPopupSource(new Slider())
            && !GraphWorkspace.IsWheelPopupSource(new TextBox())
            && !GraphWorkspace.IsWheelPopupSource(new ComboBox())
            && !GraphWorkspace.IsWheelPopupSource(new EqualizerEditor())
            && !GraphWorkspace.IsWheelPopupSource(new RotaryGainControl("Low", "200 Hz", _ => { }))
            && !GraphWorkspace.IsWheelPopupSource(new Canvas()) && !GraphWorkspace.IsWheelPopupSource(null),
            "Only popup/menu ownership protects wheel input; sliders, closed selectors, text fields, scroll viewers, EQ plots and dials all allow canvas zoom.");
        var menuLeaf = new Border(); var menuItem = new MenuItem { Header = menuLeaf }; var menu = new ContextMenu(); menu.Items.Add(menuItem);
        Require(GraphWorkspace.IsWheelPopupSource(menuLeaf) && !menu.IsOpen && PresentationSource.FromVisual(menuLeaf) is null,
            "Menu descendants remain protected without opening a context menu or creating a native window.");

        double originalZoom = mixer.GraphConfig.Zoom, originalPanX = mixer.GraphConfig.PanX, originalPanY = mixer.GraphConfig.PanY;
        workspace.ResetZoom(); await Layout(root);
        var anchor = new Point(300, 300);
        var canvas = new Canvas();
        Require(!workspace.TryZoomFromWheel(anchor, -120, popupLeaf, null)
            && !workspace.TryZoomFromWheel(anchor, -120, canvas, templatedSurface)
            && !workspace.TryZoomFromWheel(anchor, -120, canvas, null, pointerOverOpenDropDown: true)
            && mixer.GraphConfig.Zoom == 1,
            "Protected popup sources, protected hovered surfaces and the explicit open-dropdown pointer scope all leave canvas zoom unchanged and return unhandled for the popup to scroll.");
        Require(workspace.TryZoomFromWheel(anchor, -90, canvas, null) && mixer.GraphConfig.Zoom == 1
            && !workspace.TryZoomFromWheel(anchor, -30, canvas, null, pointerOverOpenDropDown: true),
            "Entering the dropdown after a partial canvas wheel notch yields the event and clears the pending canvas remainder.");
        for (int index = 0; index < 3; index++) workspace.TryZoomFromWheel(anchor, -30, canvas, null);
        Require(mixer.GraphConfig.Zoom == 1,
            "Three subsequent canvas quarter-notches do not include any wheel delta from protected dropdown scrolling.");
        Require(workspace.TryZoomFromWheel(anchor, -30, canvas, null, pointerOverOpenDropDown: false)
            && Near(mixer.GraphConfig.Zoom, .9),
            "The fourth ordinary canvas quarter-notch still zooms normally when the pointer is outside the dropdown.");
        mixer.SetCanvasView(originalPanX, originalPanY, originalZoom);
        await Layout(root);
        await CheckNodeWheelEvents(root, workspace, mixer);
        RequireNoNativeWindow(root);
    }

    private async Task CheckNewEffectsLayouts(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        double width = root.Width, height = root.Height;
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new()
        {
            Nodes = new[] { "denoiser", "deEsser", "plosive", "reverb" }
                .Select((kind, index) => GraphDefaults.NewNode(kind, 20 + index * 400, 32)).ToList(),
            Zoom = 1, PanX = 20, PanY = 20
        };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, PeakLeft = .3f, PeakRight = .12f, Channels = 2, Path = new() { State = "Running" } }).ToList();
        try
        {
            root.Width = 1680; root.Height = 720;
            mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
            var baseline = CaptureNodeLayouts(root);
            foreach (double zoom in new[] { 1d, .75, .5 })
            {
                root.Width = zoom == 1 ? 1680 : 1280;
                mixer.Edit(configuration => configuration.Graph.Zoom = zoom); await Layout(root);
                CheckProportionalNodeLayouts(root, workspace, baseline, zoom, "new effects");
                Require(Descendants<GraphNodeView>(root).All(node =>
                    LocalBounds(node, root).Right < root.Width && LocalBounds(node, root).Bottom < root.Height),
                    $"All four new effect nodes fit the detached comparison at {zoom:P0} zoom without clipping their controls.");
                Render(root, $"new-effects-{zoom * 100:0}percent.png");
                if (zoom == .75) Render(root, "new-effects-75percent-144dpi.png", 144);
            }
        }
        finally { root.Width = width; root.Height = height; await Layout(root); }
    }

    private async Task CheckNodeWheelEvents(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        var original = UiPreview.CreateSnapshot([]);
        original.Configuration.Graph = mixer.GraphConfig.Copy();
        original.Status.Nodes = mixer.GraphStatus.ToList();
        var fixture = UiPreview.CreateSnapshot([]);
        fixture.Configuration.Graph = new()
        {
            Nodes = [GraphDefaults.NewNode("microphone", 40, 40), GraphDefaults.NewNode("eq", 430, 40),
                GraphDefaults.NewNode("tone", 910, 40)], Zoom = 1
        };
        fixture.Status.Nodes = fixture.Configuration.Graph.Nodes.Select(node => new GraphNodeStatus
            { Id = node.Id, Path = new() { State = "Running" } }).ToList();
        mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
        var microphone = Descendants<GraphNodeView>(root).Single(view => fixture.Configuration.Graph.Nodes.Single(node => node.Id == view.Id).Kind == "microphone");
        var equalizer = Descendants<EqualizerEditor>(root).Single();
        var tone = Descendants<RotaryGainControl>(root).First();
        var choice = Descendants<ComboBox>(microphone).Single();
        var text = Descendants<TextBox>(microphone).First(entry => entry.Visibility == Visibility.Visible);
        var nestedScroll = Descendants<ScrollViewer>(text).First();
        var sources = new (string Name, UIElement Element)[]
        {
            ("node title", Descendants<TextBlock>(microphone).First()),
            ("closed device dropdown", Descendants<ToggleButton>(choice).First()),
            ("numeric text field", text), ("text field's nested scroll viewer", nestedScroll),
            ("level slider", Descendants<Slider>(microphone).First()),
            ("EQ plot", equalizer), ("tone dial", tone),
            ("node button", Descendants<Button>(microphone).First(button => AutomationProperties.GetName(button) == "Mute")),
            ("audio socket", microphone.FindSocket("audio", true)!)
        };
        var selectedItem = choice.SelectedItem;
        long presentations = mixer.ConfigurationPresentationCount;
        int edits = mixer.EditHistory.Entries.Count;
        var expected = mixer.GraphConfig.Copy(); expected.Zoom = 1; expected.PanX = expected.PanY = 0;
        string parameters = JsonSerializer.Serialize(expected, Protocol.Json);
        foreach (var (name, element) in sources)
        {
            workspace.ResetZoom(); await Layout(root);
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            element.RaiseEvent(wheel);
            Require(wheel.Handled && Near(mixer.GraphConfig.Zoom, .9),
                "A real detached preview wheel route over the " + name + " zooms the canvas instead of being swallowed by a node control.");
            var actual = mixer.GraphConfig.Copy(); actual.Zoom = 1; actual.PanX = actual.PanY = 0;
            Require(JsonSerializer.Serialize(actual, Protocol.Json) == parameters
                && ReferenceEquals(choice.SelectedItem, selectedItem) && !choice.IsDropDownOpen
                && mixer.ConfigurationPresentationCount == presentations && mixer.EditHistory.Entries.Count == edits,
                "Wheel zoom over the " + name + " leaves parameters, device selection and edit/save history unchanged.");
        }
        mixer.ApplyPreviewSnapshot(original); await Layout(root);
        RequireNoNativeWindow(root);
    }

    private void CheckClosedDropdownWheel()
    {
        var choice = new ComboBox { ItemsSource = new[] { "First", "Second", "Third" }, SelectedIndex = 1, Width = 200 };
        var host = new Border { Child = choice };
        host.Measure(new Size(220, 40)); host.Arrange(new Rect(0, 0, 220, 40)); host.UpdateLayout();
        Require(ReferenceEquals(choice.Style, Application.Current.FindResource(typeof(ComboBox)))
            && !choice.IsDropDownOpen && choice.Template.FindName("PART_Popup", choice) is Popup { IsOpen: false },
            "The closed-dropdown fixture uses the actual application ComboBox style and keeps its popup unopened.");
        var surface = Descendants<ToggleButton>(choice).First();
        int selectionChanges = 0; choice.SelectionChanged += (_, _) => selectionChanges++;
        object? selectedItem = choice.SelectedItem;
        foreach (int delta in new[] { 120, -120 })
        {
            var wheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, delta)
                { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            surface.RaiseEvent(wheel);
            // A handled preview prevents WPF's mouse device from promoting the
            // bubbling wheel event. Do not artificially raise a second route.
            Require(wheel.Handled && choice.SelectedIndex == 1 && ReferenceEquals(choice.SelectedItem, selectedItem)
                && selectionChanges == 0 && !choice.IsDropDownOpen,
                $"The real closed ComboBox preview route consumes wheel delta {delta:+0;-0} without changing selection, raising SelectionChanged or opening its popup.");
        }
        RequireNoNativeWindow(host);
    }

    private async Task CheckFitAtDifferentZooms(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        root.Width = 900; root.Height = 580;
        foreach (double startZoom in new[] { .3, 1d })
        {
            var fixture = UiPreview.CreateSnapshot([]);
            var eq = GraphDefaults.NewNode("eq", 30, 35);
            fixture.Configuration.Graph = new() { Nodes = [eq], Zoom = startZoom, PanX = 24, PanY = 24 };
            fixture.Status.Nodes = [new() { Id = eq.Id, Path = new() { State = "Running" } }];
            mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
            workspace.FitAll(); await Layout(root); workspace.UpdateWires(); await Layout(root);
            var view = Descendants<GraphNodeView>(root).Single();
            var viewport = Descendants<Grid>(workspace).Single(grid => grid.ClipToBounds);
            Rect bounds = view.TransformToAncestor(viewport).TransformBounds(new Rect(new Point(), view.RenderSize));
            Require(view.DetailsVisible && mixer.GraphConfig.Zoom >= .3 && bounds.Left >= 0 && bounds.Top >= 0
                && bounds.Right <= viewport.ActualWidth + .01 && bounds.Bottom <= viewport.ActualHeight + .01,
                $"Fit from {startZoom:P0} contains the complete EQ and all its editors inside the minimum-size window canvas.");
            double zoom = mixer.GraphConfig.Zoom, panX = mixer.GraphConfig.PanX, panY = mixer.GraphConfig.PanY;
            workspace.FitAll(); await Layout(root);
            Require(Math.Abs(mixer.GraphConfig.Zoom - zoom) < .000001 && Math.Abs(mixer.GraphConfig.PanX - panX) < .01
                && Math.Abs(mixer.GraphConfig.PanY - panY) < .01,
                $"Repeated Fit after starting at {startZoom:P0} retains the same proportional scale and centring. Before zoom {zoom:R}, pan ({panX:R}, {panY:R}); after zoom {mixer.GraphConfig.Zoom:R}, pan ({mixer.GraphConfig.PanX:R}, {mixer.GraphConfig.PanY:R}).");
            if (startZoom == .3) Render(root, "effects-fit-small-window.png");
        }
        root.Width = Width; root.Height = Height; await Layout(root);
    }

    private async Task CheckSelectionOutlines(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer,
        string eqId, string toneId)
    {
        double originalZoom = mixer.GraphConfig.Zoom;
        string originalSettings = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        mixer.Edit(c => c.Graph.Zoom = 1); await Layout(root);
        var expandedLayouts = CaptureNodeLayouts(root);
        workspace.SelectNode(eqId);
        workspace.SelectNodeForPointer(toneId, System.Windows.Input.ModifierKeys.Shift);
        await Layout(root);
        foreach (double zoom in new[] { .3, .5, 1d, 2d })
        {
            mixer.Edit(c => c.Graph.Zoom = zoom); await Layout(root);
            CheckProportionalNodeLayouts(root, workspace, expandedLayouts, zoom, "selected effects");
            Require(Descendants<GraphNodeView>(root).All(node => node.Id == eqId || node.Id == toneId
                    ? node.SelectionOutlineVisible && Near(node.SelectionOutlineScreenThickness, 2)
                    : !node.SelectionOutlineVisible),
                $"At {zoom:P0}, both selected nodes retain a two-screen-DIP outline and the unselected node has no selection overlay.");
            if (zoom != .5) CheckNodeGrabSurfaces(root, workspace, eqId, zoom);
            Render(root, $"selection-{zoom * 100:0}percent.png");
        }
        workspace.NodeChanged(eqId, node => node.Collapsed = true); await Layout(root);
        var collapsedLayouts = CaptureNodeLayouts(root);
        foreach (double zoom in new[] { .3, 2d })
        {
            mixer.Edit(c => c.Graph.Zoom = zoom); await Layout(root);
            CheckProportionalNodeLayouts(root, workspace, collapsedLayouts, zoom, "selection with a legacy collapsed flag");
            var eq = Descendants<GraphNodeView>(root).Single(node => node.Id == eqId);
            Require(eq.DetailsVisible && eq.SelectionOutlineVisible && Near(eq.SelectionOutlineScreenThickness, 2),
                $"At {zoom:P0}, a selected node with a legacy collapsed flag stays expanded and retains its two-screen-DIP outline.");
            if (zoom == .3) Render(root, "selection-legacy-expanded-30percent.png");
        }
        workspace.NodeChanged(eqId, node => node.Collapsed = false);
        mixer.Edit(c => c.Graph.Zoom = originalZoom);
        workspace.SelectNode(eqId); await Layout(root);
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == originalSettings
            && Descendants<GraphNodeView>(root).All(node => node.SelectionOutlineVisible == (node.Id == eqId)),
            "Selection outline verification restores the exact original graph, zoom, collapse state and single-node selection.");
    }

    private void CheckNodeGrabSurfaces(FrameworkElement root, GraphWorkspace workspace, string eqId, double zoom)
    {
        var node = Descendants<GraphNodeView>(root).Single(view => view.Id == eqId);
        Point localHaloPoint = node.GetHeaderGrabTestPoint();
        Point haloPoint = node.TranslatePoint(localHaloPoint, root);
        DependencyObject? inputHit = root.InputHitTest(haloPoint) as DependencyObject;
        DependencyObject? hit = null;
        // InputHitTest filters IsVisible, which depends on a native presentation
        // source. This fixture intentionally has none. Test actual visual hit
        // geometry while applying the interaction/visibility flags explicitly.
        VisualTreeHelper.HitTest(root,
            visual => visual is UIElement element && (element.Visibility != Visibility.Visible || !element.IsHitTestVisible)
                ? HitTestFilterBehavior.ContinueSkipSelfAndChildren : HitTestFilterBehavior.Continue,
            result => { hit = result.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(haloPoint));
        bool hitNode = false;
        var hitChain = new List<string>();
        for (var current = hit; current is not null; current = current is Visual
                 ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            hitChain.Add(current is GraphNodeView hitView ? $"GraphNodeView({hitView.Id})" : current.GetType().Name);
            if (ReferenceEquals(current, node)) { hitNode = true; break; }
        }
        Require(hitNode && node.IsNodeDragSurface(hit) && Near(node.HeaderGrabTopPadding, 8) && Near(node.HeaderGrabSidePadding, 6),
            $"At {zoom:P0}, the detached visual hit test reaches this node's forgiving header grab area, retaining eight screen DIPs above and six at each side without changing node geometry. Expected {eqId}; local halo point {localHaloPoint}; root point {haloPoint}; visual hit chain {string.Join(" > ", hitChain)}; input hit {inputHit?.GetType().Name ?? "null"}; root/node IsVisible {root.IsVisible}/{node.IsVisible}; drag accepted {node.IsNodeDragSurface(hit)}; padding {node.HeaderGrabTopPadding}/{node.HeaderGrabSidePadding}.");
        var title = Descendants<TextBlock>(node).First(text => text.Text == workspace.Graph.Nodes.Single(item => item.Id == eqId).Name);
        var body = VisualTreeHelper.GetParent(Descendants<EqualizerEditor>(node).Single());
        Require(node.IsNodeDragSurface(node) && node.IsNodeDragSurface(body) && node.IsNodeDragSurface(title),
            $"At {zoom:P0}, unused body space and the title remain node grab surfaces without injecting pointer events.");
        Require(Descendants<GraphNodeView>(root).All(view => Descendants<FrameworkElement>(view)
            .Where(element => element is ButtonBase or ComboBox or TextBox or Slider or Expander or EqualizerEditor or RotaryValueControl)
            .All(control => !view.IsNodeDragSurface(control)
                && !view.IsNodeDragSurface(Descendants<TextBlock>(control).FirstOrDefault() as DependencyObject ?? control))),
            $"At {zoom:P0}, node dragging excludes settings controls and their label descendants, including sockets, numeric fields, EQ and dials.");
    }

    private sealed record ElementLayout(Rect Bounds, bool Visible);
    private sealed record SocketLayout(double Width, double Height, Point Center, double GlyphWidth, double GlyphHeight, double Stroke);
    private sealed record NodeLayout(double Width, double Height, Thickness Border, CornerRadius Corners, double TitleFontSize,
        Dictionary<TextBlock, double> Fonts, Dictionary<FrameworkElement, ElementLayout> Editors,
        Dictionary<GraphWorkspace.Socket, SocketLayout> Sockets);

    private static Rect LocalBounds(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(new Point(), element.RenderSize));
    private static bool Near(double first, double second) => Math.Abs(first - second) < .00001;
    private static bool Near(Rect first, Rect second) => Near(first.X, second.X) && Near(first.Y, second.Y)
        && Near(first.Width, second.Width) && Near(first.Height, second.Height);

    private static SocketLayout ReadSocketLayout(GraphWorkspace.Socket socket, GraphNodeView node)
    {
        var glyph = (System.Windows.Shapes.Shape)socket.Content;
        return new(socket.ActualWidth, socket.ActualHeight,
            socket.TranslatePoint(new Point(socket.ActualWidth / 2, socket.ActualHeight / 2), node),
            glyph.ActualWidth, glyph.ActualHeight, glyph.StrokeThickness);
    }

    private static Dictionary<string, NodeLayout> CaptureNodeLayouts(FrameworkElement root) =>
        Descendants<GraphNodeView>(root).ToDictionary(node => node.Id, node => new NodeLayout(
            node.ActualWidth, node.ActualHeight, node.BorderThickness, node.CornerRadius, node.EffectiveTitleSize / node.ViewScale,
            Descendants<TextBlock>(node).ToDictionary(text => text, text => text.FontSize),
            Descendants<FrameworkElement>(node)
                .Where(element => element is ButtonBase or ComboBox or TextBox or Slider or EqualizerEditor or RotaryValueControl or GraphNodeView.NodeMeter or TextBlock)
                .ToDictionary(element => element, element => new ElementLayout(LocalBounds(element, node), EffectivelyVisible(element, node))),
            Descendants<GraphWorkspace.Socket>(node).ToDictionary(socket => socket, socket => ReadSocketLayout(socket, node))));

    private void CheckProportionalNodeLayouts(FrameworkElement root, GraphWorkspace workspace,
        Dictionary<string, NodeLayout> expected, double zoom, string fixture)
    {
        var nodes = Descendants<GraphNodeView>(root).ToArray();
        var viewport = Descendants<Grid>(workspace).Single(grid => grid.ClipToBounds);
        Point origin = viewport.TranslatePoint(new Point(), root);
        var errors = new List<string>();
        foreach (var node in nodes)
        {
            var layout = expected[node.Id];
            if (!Near(node.ActualWidth, layout.Width) || !Near(node.ActualHeight, layout.Height)
                || node.BorderThickness != layout.Border || node.CornerRadius != layout.Corners)
                errors.Add($"{node.Id}: world size/border changed from {layout.Width} x {layout.Height} to {node.ActualWidth} x {node.ActualHeight}");
            if (!Near(node.EffectiveTitleSize, layout.TitleFontSize * zoom)
                || layout.Fonts.Any(pair => !Near(pair.Key.FontSize, pair.Value)))
                errors.Add(node.Id + ": a font size changed instead of being scaled by the canvas");
            foreach (var pair in layout.Editors)
            {
                if (EffectivelyVisible(pair.Key, node) != pair.Value.Visible || !Near(LocalBounds(pair.Key, node), pair.Value.Bounds))
                { errors.Add($"{node.Id}: {pair.Key.GetType().Name} editor/label visibility or world bounds changed"); break; }
            }
            foreach (var pair in layout.Sockets)
            {
                var actual = ReadSocketLayout(pair.Key, node);
                if (!Near(actual.Width, pair.Value.Width) || !Near(actual.Height, pair.Value.Height)
                    || !Near(actual.Center.X, pair.Value.Center.X) || !Near(actual.Center.Y, pair.Value.Center.Y)
                    || !Near(actual.GlyphWidth, pair.Value.GlyphWidth) || !Near(actual.GlyphHeight, pair.Value.GlyphHeight)
                    || !Near(actual.Stroke, pair.Value.Stroke))
                { errors.Add(node.Id + ": socket position, target or glyph geometry changed in world coordinates"); break; }
            }
            var config = workspace.Graph.Nodes.Single(item => item.Id == node.Id);
            var screen = LocalBounds(node, root);
            var proportional = new Rect(origin.X + workspace.Graph.PanX + config.X * zoom,
                origin.Y + workspace.Graph.PanY + config.Y * zoom, layout.Width * zoom, layout.Height * zoom);
            if (!Near(screen, proportional)) errors.Add($"{node.Id}: transformed bounds {screen} differ from proportional bounds {proportional}");
        }
        Require(nodes.Length == expected.Count && errors.Count == 0,
            $"At {zoom:P0}, {fixture} nodes retain all world-space dimensions, fonts, borders, editor layout and socket geometry; screen bounds and title sizes scale proportionally. {string.Join("; ", errors)}");
        Require(nodes.All(node => node.DetailsVisible && Near(node.ViewScale, zoom)
            && TextOptions.GetTextFormattingMode(node) == TextFormattingMode.Ideal
            && TextOptions.GetTextRenderingMode(node) == TextRenderingMode.Grayscale
            && Descendants<TextBlock>(node).All(text => TextOptions.GetTextRenderingMode(text) == TextRenderingMode.Grayscale)),
            $"At {zoom:P0}, {fixture} shows all details with Ideal text metrics and grayscale antialiasing.");
    }

    private void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        _checks.Add(description);
    }

    private void RequireNoNativeWindow(Visual root)
    {
        Require(PresentationSource.FromVisual(root) is null,
            "The render tree is detached from every native presentation source.");
        Require(Application.Current.Windows.Cast<Window>().All(window => !window.IsVisible && new WindowInteropHelper(window).Handle == IntPtr.Zero),
            "This renderer has shown no windows and created no native window handles.");
    }

    private static async Task Layout(FrameworkElement root)
    {
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
        // Process this process's binding/layout work only. There is no engine
        // poller, preview timer, tray icon, show event or UI input automation.
        await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        root.Measure(new Size(root.Width, root.Height));
        root.Arrange(new Rect(0, 0, root.Width, root.Height));
        root.UpdateLayout();
    }

    private void Render(FrameworkElement root, string filename, double dpi = 96)
    {
        RequireNoNativeWindow(root);
        RequireNoRasterizingNodeAncestors(root);
        Visual dpiRoot = VisualRoot(root);
        DpiScale previousDpi = VisualTreeHelper.GetDpi(dpiRoot);
        try
        {
            VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(dpi / 96, dpi / 96));
            MeasureAtCurrentDpi(root, root.Width, root.Height);
            var bitmap = new RenderTargetBitmap(Pixels(root.Width, dpi), Pixels(root.Height, dpi), dpi, dpi, PixelFormats.Pbgra32);
            bitmap.Render(root);
            RecordDpi(root, filename, dpi, root.Width, root.Height);
            _zoomEvidence.Add(new
            {
                file = filename, dpi,
                nodes = Descendants<GraphNodeView>(root).Select(node => new
                {
                    node.Id, zoom = node.ViewScale, detailsVisible = node.DetailsVisible,
                    worldWidth = node.ActualWidth, worldHeight = node.ActualHeight,
                    titleScreenDips = node.EffectiveTitleSize, titlePhysicalPixels = node.EffectiveTitleSize * dpi / 96,
                    screenBounds = node.TransformToAncestor(root).TransformBounds(new Rect(new Point(), node.RenderSize)).ToString()
                }).ToArray(),
                scope = "Measured actual WPF canvas transforms and text size. PNGs are rendered at their target DPI, not resized after capture."
            });
            SaveBitmap(bitmap, filename);
        }
        finally
        {
            VisualTreeHelper.SetRootDpi(dpiRoot, previousDpi);
            MeasureAtCurrentDpi(root, root.Width, root.Height);
        }
    }

    private void RenderMenu(ContextMenu menu, string filename)
    {
        // Render the real menu tree without opening its Popup or a native window.
        menu.Visibility = Visibility.Visible; menu.ApplyTemplate();
        // The same Settings menu is captured with several queries. Let each
        // filtered tree shrink before fixing its detached bitmap dimensions.
        menu.Height = double.NaN;
        menu.InvalidateMeasure();
        menu.Measure(new Size(440, 520));
        menu.Width = Math.Max(280, menu.DesiredSize.Width);
        // Fix the width before measuring wrapped item headers and feedback.
        menu.Measure(new Size(menu.Width, 520));
        menu.Height = Math.Min(menu.MaxHeight, Math.Max(80, menu.DesiredSize.Height + 4));
        Render(menu, filename);
        Require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null, "Menu preview remains detached from the desktop.");
    }

    private void RenderNode(GraphNodeView node, GraphWorkspace workspace, string filename, double dpi = 96, double? availabilityPulseSeconds = null)
    {
        RequireNoNativeWindow(node);
        // A VisualBrush of a Canvas child carries its layout offset into the
        // source coordinates. Render a fresh instance of the actual node control
        // in a small memory-only host instead. The original node never moves or
        // loses bindings, and this host has no canvas position/zoom to inherit.
        var model = workspace.Graph.Nodes.Single(item => item.Id == node.Id).Copy();
        var closeup = new GraphNodeView(workspace, model);
        closeup.Apply(model, workspace.Mixer.GraphStatus.FirstOrDefault(item => item.Id == node.Id),
            node.SelectionOutlineVisible);
        foreach (var expander in Descendants<Expander>(node))
        {
            var matching = Descendants<Expander>(closeup).FirstOrDefault(item => AutomationProperties.GetName(item) == AutomationProperties.GetName(expander));
            if (matching is not null) matching.IsExpanded = expander.IsExpanded;
        }
        const double padding = 18;
        int width = (int)Math.Ceiling(node.ActualWidth + padding * 2);
        var host = new Border
        {
            Width = width, Padding = new Thickness(padding), Child = closeup,
            DataContext = workspace.Mixer, Background = ConsoleTheme.Get("NodeCanvasBrush"),
            UseLayoutRounding = true, SnapsToDevicePixels = true
        };
        TextElement.SetFontFamily(host, TextElement.GetFontFamily(node));
        TextElement.SetFontSize(host, TextElement.GetFontSize(node));
        TextElement.SetForeground(host, TextElement.GetForeground(node));
        TextOptions.SetTextFormattingMode(host, TextOptions.GetTextFormattingMode(node));
        TextOptions.SetTextRenderingMode(host, TextOptions.GetTextRenderingMode(node));
        TextOptions.SetTextHintingMode(host, TextOptions.GetTextHintingMode(node));
        RenderOptions.SetClearTypeHint(host, RenderOptions.GetClearTypeHint(node));
        // Updating WPF's actual visual DPI makes text, layout rounding and custom
        // drawing receive the requested PixelsPerDip; this is not a resized PNG.
        VisualTreeHelper.SetRootDpi(host, new DpiScale(dpi / 96, dpi / 96));
        host.Measure(new Size(width, double.PositiveInfinity));
        int height = (int)Math.Ceiling(host.DesiredSize.Height);
        host.Height = height;
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        RequireNoNativeWindow(host);
        RequireNoRasterizingNodeAncestors(host);
        Require(Math.Abs(closeup.ActualWidth - node.ActualWidth) < .01 && closeup.ActualHeight > 0,
            $"The {dpi:0}-DPI EQ close-up uses the actual control at its logical width, independently of canvas zoom.");
        if (dpi == 96)
            Require(Math.Abs(closeup.ActualHeight - node.ActualHeight) < 1,
                "The 100% EQ close-up preserves the actual node's expanded/collapsed dimensions without reparenting the original view.");
        RecordDpi(closeup, filename, dpi, width, height);
        if (availabilityPulseSeconds is double seconds) closeup.SeekAvailabilityWarningPulse(seconds);
        var bitmap = new RenderTargetBitmap(Pixels(width, dpi), Pixels(height, dpi), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(host);
        SaveBitmap(bitmap, filename);
    }

    private static int Pixels(double logicalSize, double dpi) => checked((int)Math.Ceiling(logicalSize * dpi / 96));

    private static Visual VisualRoot(Visual visual)
    {
        Visual root = visual;
        while (VisualTreeHelper.GetParent(root) is Visual parent) root = parent;
        return root;
    }

    private static void MeasureAtCurrentDpi(FrameworkElement root, double width, double height)
    {
        root.InvalidateMeasure(); root.InvalidateArrange(); root.InvalidateVisual();
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private void RequireNoRasterizingNodeAncestors(FrameworkElement root)
    {
        foreach (var node in Descendants<GraphNodeView>(root))
        {
            DependencyObject? current = node;
            while (current is not null)
            {
                if (current is UIElement element && (element.Effect is not null || element.CacheMode is BitmapCache))
                    throw new InvalidOperationException("A node or its ancestor rasterizes the full node through an Effect or BitmapCache, including its text.");
                current = VisualTreeHelper.GetParent(current);
            }
        }
        _checks.Add("Node text has no full-node or ancestor Effect/BitmapCache forcing rasterization before output DPI scaling.");
    }

    private void RecordDpi(FrameworkElement visual, string filename, double dpi, double logicalWidth, double logicalHeight)
    {
        DpiScale actual = VisualTreeHelper.GetDpi(visual);
        Require(Math.Abs(actual.PixelsPerInchX - dpi) < .01 && Math.Abs(actual.PixelsPerInchY - dpi) < .01,
            filename + $" uses actual WPF visual DPI {dpi:0}, with independently rasterized text and controls.");
        var nodes = Descendants<GraphNodeView>(visual).ToList();
        if (visual is GraphNodeView ownNode) nodes.Add(ownNode);
        Require(nodes.All(node => Math.Abs(VisualTreeHelper.GetDpi(node).PixelsPerInchX - dpi) < .01),
            filename + " propagates the requested DPI into every node control.");
        _dpiEvidence.Add(new
        {
            file = filename, dpi, scale = dpi / 96, logicalWidth, logicalHeight,
            expectedPixelWidth = Pixels(logicalWidth, dpi), expectedPixelHeight = Pixels(logicalHeight, dpi),
            observedVisualDpiX = actual.PixelsPerInchX, observedVisualDpiY = actual.PixelsPerInchY,
            textFormattingMode = TextOptions.GetTextFormattingMode(visual).ToString(),
            textRenderingMode = TextOptions.GetTextRenderingMode(visual).ToString(),
            scope = "Offscreen WPF DPI/layout/rasterization; not a physical-monitor DPI transition or subjective sharpness measurement."
        });
    }

    private void SaveBitmap(RenderTargetBitmap bitmap, string filename)
    {
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight;
        byte[] pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var colours = new HashSet<int>();
        for (int offset = 0; offset < pixels.Length; offset += 4 * 17)
            colours.Add(BitConverter.ToInt32(pixels, offset));
        // Sparse text may fall between samples. A small, correctly rasterized
        // Plex label can also use fewer than sixteen colours, so check actual
        // contrasting ink rather than requiring a particular antialias palette.
        bool contrastingInk = false;
        if (colours.Count < 16)
        {
            for (int offset = 0; offset < pixels.Length && colours.Count < 16; offset += 4)
                colours.Add(BitConverter.ToInt32(pixels, offset));
            if (colours.Count < 16)
            {
                var histogram = new Dictionary<int, int>();
                for (int offset = 0; offset < pixels.Length; offset += 4)
                {
                    int colour = BitConverter.ToInt32(pixels, offset);
                    histogram[colour] = histogram.GetValueOrDefault(colour) + 1;
                }
                byte[] background = BitConverter.GetBytes(histogram.MaxBy(pair => pair.Value).Key);
                int ink = 0, left = width, right = 0, top = height, bottom = 0;
                for (int offset = 0; offset < pixels.Length; offset += 4)
                {
                    if (pixels[offset + 3] < 128 || Math.Abs(pixels[offset] - background[0])
                        + Math.Abs(pixels[offset + 1] - background[1]) + Math.Abs(pixels[offset + 2] - background[2]) < 120) continue;
                    int x = offset / 4 % width, y = offset / 4 / width;
                    ink++; left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
                contrastingInk = ink >= 24 && right - left >= 8 && bottom - top >= 4;
            }
        }
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = new FileStream(Path.Combine(_directory, filename), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        png.Save(file);
        Require(colours.Count >= 16 || contrastingInk, filename + $" contains a nonblank {width} x {height} rendered layout at {bitmap.DpiX:0} DPI ({colours.Count} sampled colours; contrasting text checked when sparse).");
        _images.Add(filename);
        _imageSizes.Add(new { file = filename, width, height, dpiX = bitmap.DpiX, dpiY = bitmap.DpiY,
            logicalWidth = width * 96 / bitmap.DpiX, logicalHeight = height * 96 / bitmap.DpiY });
    }

    private void CheckEqualizerEdits(GraphNodeView node, MixerViewModel mixer)
    {
        var editor = Descendants<EqualizerEditor>(node).Single();
        string original = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        Require(editor.SelectBand(-1) && editor.SelectedBandIndex == -1
            && AutomationProperties.GetItemStatus(editor).Contains("No band selected", StringComparison.Ordinal)
            && Descendants<Slider>(node).Where(slider => AutomationProperties.GetName(slider) is "Frequency" or "Band gain" or "Width / Q").All(slider => !slider.IsEnabled),
            "Deselecting an EQ point survives the owner's synchronous refresh and presents a truthful selection hint.");
        editor.SelectBand(0);
        var expected = mixer.GraphConfig.Copy();
        var expectedNode = expected.Nodes.Single(item => item.Id == node.Id);
        expectedNode.EqBands[2].FrequencyHz = 2400; expectedNode.EqBands[2].GainDb = -9;
        Require(editor.MoveBand(2, 2400, -9)
            && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json),
            "Moving an actual EQ graph point updates only that band's frequency/gain through the node binding, preserving routes and other nodes.");
        Require(editor.AddBandAt(3200, 6) && mixer.GraphConfig.Nodes.Single(item => item.Id == node.Id).EqBands.Count == 6,
            "Adding an EQ point through the actual editor commits a sixth band to the in-memory graph.");
        Require(editor.RemoveSelectedBand()
            && JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json),
            "Removing the selected added point restores all original bands without deleting the node or its connections.");
        editor.MoveBand(2, 1800, -6); editor.SelectBand(0);
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == original,
            "The complete in-memory effects graph is restored after point editing.");
        var isolated = new EqualizerEditor(); isolated.Apply([], false, -1);
        for (int index = 0; index < 8; index++) Require(isolated.AddBandAt(100 * Math.Pow(1.8, index), index - 4), "The isolated EQ accepts point " + (index + 1) + ".");
        string full = JsonSerializer.Serialize(isolated.SnapshotBands(), Protocol.Json);
        Require(!isolated.AddBandAt(12000, 3) && isolated.BandCount == 8 && JsonSerializer.Serialize(isolated.SnapshotBands(), Protocol.Json) == full,
            "A ninth EQ point is rejected without changing the eight existing bands.");
        while (isolated.BandCount > 0) Require(isolated.RemoveSelectedBand(), "Removing an isolated point preserves valid selection.");
        Require(isolated.BandCount == 0 && !isolated.RemoveSelectedBand(), "An EQ can return to zero bands for a flat response.");
    }

    private void CheckToneEdits(GraphNodeView node, MixerViewModel mixer)
    {
        string original = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        var expected = mixer.GraphConfig.Copy();
        var model = expected.Nodes.Single(item => item.Id == node.Id);
        double previous = model.EqBands[1].GainDb;
        model.EqBands[1].GainDb = 4.5;
        var middle = Descendants<Slider>(node).Single(slider => AutomationProperties.GetName(slider) == "Mid");
        middle.Value = 4.5;
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json),
            "Changing the actual Mid dial updates only the tone node's middle gain, preserving its fixed filters, other nodes and routes.");
        middle.Value = previous;
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == original,
            "Restoring the tone dial restores the complete in-memory effects graph.");
    }

    private void CheckConfigurationRefreshDecisions()
    {
        static MixerSnapshot Fixture() => new()
        {
            Configuration = new() { Version = 2, Revision = 7 },
            Status = new() { AudioFrames = 48000 },
            SettingsPath = "in-memory-fixture/settings.json"
        };
        static bool Refresh(MixerSnapshot? previous, MixerSnapshot next, bool preview = false, bool connected = true, bool force = false)
            => MixerViewModel.RequiresConfigurationRefresh(previous, next, preview, connected, force);

        var before = Fixture(); var next = Fixture();
        next.Status.AudioFrames += 240;
        Require(Refresh(null, next), "An initial in-memory snapshot requires the committed configuration to be copied.");
        Require(!Refresh(before, next), "An advancing telemetry snapshot with the same revision skips configuration copying and pending-edit replay.");
        next.Configuration.Revision++;
        Require(Refresh(before, next), "A changed committed revision refreshes configuration so pending edits can be rebased.");
        next = Fixture(); next.Configuration.Revision--;
        Require(Refresh(before, next), "A revision moving backwards also refreshes configuration after an engine replacement or restored settings.");
        next = Fixture();
        Require(Refresh(before, next, connected: false), "Reconnection refreshes configuration even when the saved revision is unchanged.");
        next.Status.AudioFrames = 0;
        Require(Refresh(before, next), "A backwards audio-frame counter forces configuration refresh for an engine reset.");
        next = Fixture();
        Require(Refresh(before, next, force: true), "Configure acknowledgement or rejection can force reconciliation without a revision change.");
        next.Configuration.Graph.Nodes.Add(GraphDefaults.NewNode("gain", 71, 89));
        Require(Refresh(before, next, preview: true), "A replacement preview fixture is applied even when its revision matches the previous fixture.");
        next = Fixture(); next.SettingsPath = "another-in-memory-fixture/settings.json";
        Require(Refresh(before, next), "A different settings identity refreshes configuration without accessing that path.");
        next = Fixture(); next.Configuration.StartAtLogin = true;
        Require(Refresh(before, next), "A changed start-at-login setting is reconciled even if a snapshot repeats its revision.");
        next = Fixture(); next.Status.PeakA = .4f; next.Status.ClipA = true;
        next.Endpoints.Add(new() { Id = "fixture-output", Name = "Fixture output", Direction = "render", State = "Active" });
        next.Applications.Add(new() { ProcessId = 123, Name = "Fixture app", ExecutablePath = "in-memory-fixture/app.exe" });
        Require(!Refresh(before, next), "Meter, endpoint inventory and application discovery changes do not alone require a configuration clone.");

        using var previewModel = new MixerViewModel(preview: true);
        previewModel.ApplyPreviewSnapshot(before);
        long copies = previewModel.ConfigurationRefreshCount, controls = previewModel.ConfigurationPresentationCount;
        int graphUpdates = 0;
        previewModel.GraphChanged += (_, _) => graphUpdates++;
        before.Status.Nodes.Add(new() { Id = "preview-mix", Peak = .4f, Clipped = true, Path = new() { State = "Running" } });
        previewModel.RefreshPreviewTelemetry();
        Require(previewModel.ConfigurationRefreshCount == copies && previewModel.ConfigurationPresentationCount == controls
            && previewModel.GraphStatus.Single().Peak == .4f && previewModel.GraphStatus.Single().Clipped && graphUpdates == 1,
            "An in-memory telemetry update publishes fresh node meters, clipping and graph status without copying configuration or maintaining retired mixer controls.");
        next = Fixture(); next.Configuration.Graph.Nodes.Add(GraphDefaults.NewNode("gain", 71, 89)); next.Configuration.StartAtLogin = true;
        previewModel.ApplyPreviewSnapshot(next);
        Require(previewModel.ConfigurationRefreshCount == copies + 1 && previewModel.GraphConfig.Nodes.Count == 1
            && previewModel.GraphConfig.Nodes[0].X == 71 && previewModel.StartAtLogin,
            "Applying a same-revision replacement fixture updates its graph and startup presentation only in the inert preview model.");
    }

    private void CheckEditDispatchCadence()
    {
        // Exercise the same scheduler used by the live model, but with a
        // deterministic clock and a fake serial request. No pipe, delay, disk
        // settings, or engine is involved. Edits outpace both polling and IPC.
        static TimeSpan At(int milliseconds) => TimeSpan.FromMilliseconds(milliseconds);
        var cadence = new EditDispatchCadence();
        Require(!cadence.IsDue(At(0)), "An idle edit scheduler does not dispatch without pending changes.");
        var pending = new List<int>();
        var sentAt = new List<int>();
        int generated = 0, committed = 0, committedCount = 0;
        int completionAt = -1, capturedCount = 0, capturedLast = 0;
        bool editsSurvivedInFlight = false;
        for (int now = 0; now <= 2300; now++)
        {
            if (now < 2000 && now % 8 == 0)
            {
                pending.Add(++generated);
                cadence.Queue(At(now));
            }
            if (now == completionAt)
            {
                committed = capturedLast; committedCount += capturedCount;
                pending.RemoveRange(0, capturedCount);
                editsSurvivedInFlight |= pending.Count > 0;
                cadence.Complete(pending.Count > 0);
                completionAt = -1;
            }
            if (now % 33 == 0 && completionAt < 0 && pending.Count > 0 && cadence.IsDue(At(now)))
            {
                capturedCount = pending.Count; capturedLast = pending[^1];
                cadence.BeginDispatch(At(now));
                sentAt.Add(now); completionAt = now + 70;
            }
        }
        Require(sentAt.Count >= 14 && sentAt[0] <= 133 && sentAt.Count(time => time < 1900) >= 13,
            "Two seconds of edits every 8 ms dispatch throughout the gesture, without waiting for editing to stop.");
        Require(sentAt.Zip(sentAt.Skip(1), (first, next) => next - first).All(gap => gap >= 100 && gap <= 133),
            "With 33 ms polling and a 70 ms fake request, continuous edits dispatch 100–133 ms apart without flooding requests.");
        Require(editsSurvivedInFlight && generated == 250 && committedCount == generated && committed == generated && pending.Count == 0,
            "Acknowledging only each captured batch preserves edits arriving during a request and eventually commits the final value exactly once.");
        Require(!cadence.IsDue(At(3000)), "Finishing the last pending batch returns the edit scheduler to idle.");

        cadence.Queue(At(4000)); cadence.Queue(At(4099));
        Require(!cadence.IsDue(At(4099)) && cadence.IsDue(At(4100)),
            "A new gesture coalesces for 100 ms, and a later edit cannot postpone its first dispatch.");
        cadence.BeginDispatch(At(4100));
        cadence.DeferRetry(At(4170));
        for (int now = 4171; now < 4270; now++) cadence.Queue(At(now));
        Require(!cadence.IsDue(At(4269)) && cadence.IsDue(At(4270)),
            "A revision-conflict retry keeps its 100 ms backoff even while new edits continue to arrive.");
        cadence.BeginDispatch(At(4270));
        cadence.Queue(At(4300)); cadence.Complete(hasPending: true);
        Require(cadence.IsDue(At(4500)),
            "When a slow request finishes after the next deadline, surviving edits are immediately eligible instead of starting another debounce.");
        cadence.Complete(hasPending: false);
    }

    private void CheckStereoMeters()
    {
        var meter = new GraphNodeView.NodeMeter { Width = 28, Height = 180 };
        meter.Measure(new Size(28, 180)); meter.Arrange(new Rect(0, 0, 28, 180));
        meter.Update(.8, .2, 2, false, false, false, 0);
        var lanes = meter.GetLaneBounds();
        Require(meter.LeftLevel == .8 && meter.RightLevel == .2 && meter.Level == .8 && meter.Channels == 2,
            "Stereo telemetry presents independent asymmetric left and right levels instead of duplicating a combined peak.");
        Require(lanes.Length == 2 && lanes.All(lane => lane.Width >= 10 && lane.Height > lane.Width * 4)
            && lanes[0].Right < lanes[1].Left && lanes[0].Top == lanes[1].Top && lanes[0].Bottom == lanes[1].Bottom,
            "The 28-DIP meter draws two separate thick vertical lanes with aligned ranges.");
        meter.Update(.9, .7, 2, false, false, false, .01);
        Require(meter.LeftLevel == .9 && meter.RightLevel == .7,
            "A rising peak reaches each stereo lane immediately without waiting for a decay timer.");

        var coarse = new GraphNodeView.NodeMeter(); var fine = new GraphNodeView.NodeMeter();
        foreach (var release in new[] { coarse, fine })
        {
            release.Update(1, .5, 2, false, false, false, 0);
            release.Update(0, 0, 2, false, false, false, 0);
        }
        coarse.Advance(.05);
        Require(coarse.LeftLevel == 1 && coarse.RightLevel == .5,
            "A falling signal briefly holds its peak instead of dropping immediately with the next telemetry sample.");
        coarse.Advance(.6);
        for (int step = 1; step <= 60; step++) fine.Advance(step / 100d);
        double expectedRelease = Math.Pow(10, -24 * .5 / 20);
        Require(Math.Abs(coarse.LeftLevel - expectedRelease) < .000001 && Math.Abs(coarse.RightLevel - expectedRelease * .5) < .000001
            && Math.Abs(fine.LeftLevel - coarse.LeftLevel) < .000001 && Math.Abs(fine.RightLevel - coarse.RightLevel) < .000001,
            $"Meter fall follows 24 dB per second after its 100 ms hold, with the same result for 10 ms ticks or one larger elapsed-time step. Coarse {coarse.LeftLevel:R}/{coarse.RightLevel:R}; fine {fine.LeftLevel:R}/{fine.RightLevel:R}; expected {expectedRelease:R}/{expectedRelease * .5:R}.");
        coarse.Advance(4);
        Require(coarse.LeftLevel == 0 && coarse.RightLevel == 0,
            "Silence drains both meter tails to zero within bounded time rather than leaving an indefinite residual.");
        meter.Update(.6, .9, 1, false, false, false, .02);
        Require(meter.Channels == 1 && meter.GetLaneBounds().Length == 1 && meter.LeftLevel == .6 && meter.RightLevel == 0,
            "Mono telemetry uses one centred lane from its measured channel and does not invent a second channel.");
        meter.Update(1.1, .7, 2, true, false, false, .03);
        meter.Update(.6, .5, 2, false, true, false, .04);
        Require(meter.Muted && !meter.Clipped && meter.LeftLevel == 0 && meter.RightLevel == 0,
            "Muting clears both stereo lanes and clipping immediately, without a leftover decay tail.");
        meter.Update(double.NaN, double.PositiveInfinity, 2, false, false, false, .05);
        Require(meter.LeftLevel == 0 && meter.RightLevel == 0,
            "Non-finite channel readings remain safe silence instead of producing invalid meter geometry.");

        var reduced = new GraphNodeView.NodeMeter();
        reduced.Update(.1, .05, 2, false, false, true, 0);
        reduced.Update(.85, .15, 2, false, false, true, .06);
        reduced.Update(.2, .65, 2, false, false, true, .2);
        reduced.Advance(.24);
        Require(reduced.LeftLevel == .1 && reduced.RightLevel == .05,
            "Reduced motion holds both lane presentations between its four updates per second.");
        reduced.Advance(.251);
        Require(reduced.LeftLevel == .85 && reduced.RightLevel == .65,
            "Reduced motion preserves each channel's greatest observed peak even when the two peaks arrive in different samples.");
        reduced.Update(1.2, .1, 2, true, false, true, .26);
        Require(reduced.Clipped && reduced.Level == 1.2,
            "Reduced motion still presents clipping immediately.");
        reduced.Update(.3, .2, 2, false, true, true, .27);
        Require(reduced.Muted && !reduced.Clipped && reduced.Level == 0,
            "Reduced motion still clears the display immediately on mute.");
        reduced.Update(.3, .15, 2, false, false, false, .28);
        Require(!reduced.ReducedAnimation && reduced.LeftLevel == .3 && reduced.RightLevel == .15,
            "Leaving reduced motion starts from the current measured channels instead of an old accumulated peak.");
        Require(new[] { meter, coarse, fine, reduced }.All(item => !item.IsAnimationRunning),
            "Detached meter fixtures advance only through explicit test timestamps and start no presentation timers.");
        RequireNoNativeWindow(meter);
    }

    private async Task CheckMicrophoneControls(FrameworkElement root, GraphWorkspace workspace, MixerViewModel mixer)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        var microphone = GraphDefaults.NewNode("microphone", 40, 35);
        Require(microphone.Device.Channel == -1 && !microphone.Mono,
            "A newly created microphone preserves stereo by default, with Mono off and no legacy single-channel selection.");
        microphone.Device.Id = "preview-stereo-microphone"; microphone.Device.Name = "Stereo microphone";
        fixture.Endpoints.Add(new() { Id = microphone.Device.Id, Name = microphone.Device.Name, Direction = "capture", Channels = 2, Rate = 48000, State = "Active" });
        fixture.Configuration.Graph = new() { Nodes = [microphone], Zoom = 1, PanX = 20, PanY = 20 };
        var status = new GraphNodeStatus { Id = microphone.Id, Peak = .8f, PeakLeft = .8f, PeakRight = .15f, Channels = 2,
            Path = new() { State = "Running", Channels = 2, Rate = 48000, EndpointId = microphone.Device.Id } };
        fixture.Status.Nodes = [status];
        mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
        var view = Descendants<GraphNodeView>(root).Single();
        var mono = Descendants<Button>(view).Single(button => AutomationProperties.GetName(button) == "Mono");
        var mute = Descendants<Button>(view).Single(button => Equals(button.Content, "Mute"));
        var device = Descendants<ComboBox>(view).Single();
        var level = Descendants<RotaryValueControl>(view).Single();
        var meter = Descendants<GraphNodeView.NodeMeter>(view).Single();
        Rect Bounds(FrameworkElement element) => new(element.TranslatePoint(new Point(), view), element.RenderSize);
        var rows = new FrameworkElement[] { device, mono, level, mute }.Select(Bounds).ToArray();
        Require(AutomationProperties.GetItemStatus(mono) == "Off" && !Descendants<ComboBox>(view).Any(choice => AutomationProperties.GetName(choice) == "Microphone channel"),
            "The microphone presents an accessible off-state Mono button in place of its old channel dropdown.");
        Require(rows.All(row => Math.Abs(row.Left - rows[0].Left) < .01 && Math.Abs(row.Width - rows[0].Width) < .01)
            && rows.Zip(rows.Skip(1), (first, next) => first.Bottom <= next.Top).All(separate => separate),
            "Device, Mono, Level and Mute occupy separate aligned full-width rows without overlapping.");
        Require(meter.ActualWidth == 28 && meter.ActualHeight >= 90 && Bounds(meter).Right < rows[0].Left
            && meter.Channels == 2 && Math.Abs(meter.LeftLevel - .8) < .000001 && Math.Abs(meter.RightLevel - .15) < .000001,
            "The microphone's thicker vertical meter sits to the left of its controls and receives actual independent L/R telemetry.");
        RenderNode(view, workspace, "microphone-stereo-closeup.png");

        var expected = mixer.GraphConfig.Copy(); expected.Nodes[0].Mono = true;
        mono.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(root);
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json)
            && AutomationProperties.GetItemStatus(mono) == "On",
            "Activating the actual Mono button updates only this fixture microphone's Mono setting and its visible on-state.");
        fixture.Configuration.Graph = mixer.GraphConfig.Copy(); status.Channels = 1; status.PeakLeft = status.PeakRight = status.Peak = .475f;
        mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
        Require(meter.Channels == 1 && meter.GetLaneBounds().Length == 1,
            "The microphone switches to a single meter lane when the engine reports its mono result.");
        RenderNode(view, workspace, "microphone-mono-closeup.png");
        mono.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(root);
        Require(!mixer.GraphConfig.Nodes[0].Mono && mixer.GraphConfig.Nodes[0].Device.Channel == -1 && AutomationProperties.GetItemStatus(mono) == "Off",
            "Switching Mono off returns the microphone to stereo without selecting a particular input channel.");
        fixture.Configuration.Graph = mixer.GraphConfig.Copy(); fixture.Configuration.Graph.Nodes[0].Device.Channel = 1;
        string legacy = JsonSerializer.Serialize(fixture.Configuration.Graph, Protocol.Json);
        mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == legacy && AutomationProperties.GetItemStatus(mono) == "On",
            "Loading a saved single-channel microphone preserves that selection and presents its effective mono state without silently changing configuration.");
        expected = mixer.GraphConfig.Copy(); expected.Nodes[0].Device.Channel = -1;
        mono.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(root);
        Require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == JsonSerializer.Serialize(expected, Protocol.Json)
            && AutomationProperties.GetItemStatus(mono) == "Off",
            "Turning off a saved single-channel microphone's Mono state explicitly restores stereo while retaining all other settings.");
        fixture.Configuration.Graph = mixer.GraphConfig.Copy(); status.Path.State = "Waiting";
        mixer.ApplyPreviewSnapshot(fixture); await Layout(root);
        Require(meter.Muted && meter.LeftLevel == 0 && meter.RightLevel == 0,
            "A waiting microphone clears both meter lanes immediately even if the last telemetry sample still contains a peak.");
        RequireNoNativeWindow(root);
    }

    private void CheckNumericResetAndEntryTemplates()
    {
        int levelChanges = 0, toneChanges = 0, frequencyChanges = 0;
        double level = -12, tone = 4, frequency = 4200;
        var number = new RotaryValueControl("Reset gain", -60, 12, .5, "dB", 0, value => { level = value; levelChanges++; }, tile: true);
        var dial = new RotaryGainControl("Reset tone", "1 kHz", value => { tone = value; toneChanges++; });
        var hz = new RotaryValueControl("Reset frequency", 20, 20000, 1, "Hz", 1000,
            value => { frequency = value; frequencyChanges++; }, logarithmic: true, tile: true);
        number.SetValue(level); dial.SetValue(tone); hz.SetValue(frequency);
        var controls = new StackPanel(); controls.Children.Add(number); controls.Children.Add(dial); controls.Children.Add(hz);
        var host = new Border { Child = controls };
        host.Measure(new Size(320, double.PositiveInfinity)); host.Arrange(new Rect(0, 0, 320, host.DesiredSize.Height)); host.UpdateLayout();
        var numberEntry = Descendants<TextBox>(number).Single();
        var dialEntry = Descendants<TextBox>(dial).Single();
        var frequencyEntry = Descendants<TextBox>(hz).Single();
        foreach (var entry in new[] { numberEntry, dialEntry, frequencyEntry })
        {
            var edge = Descendants<Border>(entry).Single(border => ReferenceEquals(border.TemplatedParent, entry));
            Require(ReferenceEquals(entry.Style, Application.Current.FindResource("NodeNumericEntry"))
                && entry.Template.FindName("FocusEdge", entry) is null
                && entry.Template.FindName("PART_ContentHost", entry) is ScrollViewer
                && !entry.IsReadOnly && entry.Focusable && edge.BorderThickness == entry.BorderThickness
                && edge.BorderThickness.Left > 0,
                $"{AutomationProperties.GetName(entry)} uses the actual numeric-entry template, retaining its editable text host and normal border without a bright focus overlay.");
            entry.Text = "invalid pending"; entry.BorderBrush = ConsoleTheme.Get("ConsoleMuteBrush");
            host.UpdateLayout();
            Require(SameColour(edge.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush")),
                $"{AutomationProperties.GetName(entry)} retains the template-bound validation-error border.");
        }

        number.BeginAdjustment(0); number.ResetToDefault();
        number.ContinueAdjustment(100, fine: false); number.ResetToDefault();
        Require(level == 0 && levelChanges == 1 && numberEntry.Text == 0d.ToString("0.0") + " dB"
            && SameColour(numberEntry.BorderBrush, Brushes.Transparent),
            "Resetting a changed numeric gain restores zero and its text, clears its error, cancels the active drag and emits only one change even when reset again.");
        dial.BeginAdjustment(0); dial.ResetToDefault();
        dial.ContinueAdjustment(-100, fine: false); dial.ResetToDefault();
        host.UpdateLayout();
        Require(tone == 0 && dial.Value == 0 && toneChanges == 1 && dialEntry.Text == 0d.ToString("0.0") + " dB"
            && SameColour(dialEntry.BorderBrush, Brushes.Transparent),
            "Resetting a changed tone dial restores zero and its text, clears its error, cancels the active drag and emits only one change even when reset again.");

        foreach (var entry in new[] { numberEntry, dialEntry })
        {
            entry.Text = "invalid pending"; entry.BorderBrush = ConsoleTheme.Get("ConsoleMuteBrush");
            var doubleClick = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0,
                System.Windows.Input.MouseButton.Left) { RoutedEvent = Control.PreviewMouseDoubleClickEvent };
            // Raise only the control's local route; never inject pointer input,
            // request focus, capture the mouse or construct a native window.
            entry.RaiseEvent(doubleClick);
            host.UpdateLayout();
            Require(doubleClick.Handled && entry.Text == 0d.ToString("0.0") + " dB"
                && entry.SelectionLength == entry.Text.Length && levelChanges == 1 && toneChanges == 1
                && !SameColour(entry.BorderBrush, ConsoleTheme.Get("ConsoleMuteBrush")),
                $"Double-clicking {AutomationProperties.GetName(entry)} restores invalid pending text even at the default, selects the result and emits no duplicate value change.");
        }
        hz.ResetToDefault(); hz.ResetToDefault();
        Require(frequency == 1000 && frequencyChanges == 1 && frequencyEntry.Text == "1000 Hz",
            "A logarithmic frequency field resets to its explicit 1000 Hz default, rather than zero or the range minimum, with one change notification.");
        RequireNoNativeWindow(host);
    }

    private void CheckRotaryAdjustment()
    {
        int callbacks = 0; double gain = 0;
        var dial = new RotaryGainControl("Test", "200 Hz", value => { gain = value; callbacks++; });
        dial.SetValue(0); Require(callbacks == 0, "Refreshing a tone dial never sends an audio control change.");
        dial.BeginAdjustment(0); dial.ContinueAdjustment(-30, false);
        Require(Math.Abs(gain - 3) < .01, "Dragging a tone dial upwards increases its gain relatively.");
        dial.ContinueAdjustment(-30, true); Require(Math.Abs(gain - 3) < .01, "Switching a dial to fine adjustment without movement does not jump its value.");
        dial.ContinueAdjustment(-35, true); Require(Math.Abs(gain - 3.1) < .01, "Fine dial adjustment changes only the new movement.");
        Require(dial.TryCommitText("-4.5 dB") && Math.Abs(gain + 4.5) < .01, "A tone dial accepts precise numeric gain entry.");
        Require(!dial.TryCommitText("NaN") && !dial.TryCommitText("13") && Math.Abs(gain + 4.5) < .01,
            "Invalid and out-of-range tone entries leave the prior gain intact.");
    }

    private void CheckNumericAdjustment()
    {
        double level = -24;
        var control = new RotaryValueControl("Drag regression", -60, 12, .5, "dB", 0, value => level = value, tile: true);
        control.SetValue(level);
        control.BeginAdjustment(0); control.ContinueAdjustment(-72, fine: false);
        double coarse = level;
        Require(Math.Abs(coarse - (-2.5)) < .01, "An in-memory vertical dial drag changes the level relatively from its starting value.");
        control.ContinueAdjustment(-72, fine: true);
        Require(level == coarse, "Pressing Shift with no new pointer displacement leaves the numeric value unchanged.");
        control.ContinueAdjustment(-73, fine: true);
        double fine = level;
        Require(Math.Abs(fine - coarse) <= .5, "One pixel of fine adjustment after a coarse drag stays within one level step, without a large value jump.");
        control.ContinueAdjustment(-73, fine: false);
        Require(level == fine, "Releasing Shift with no new pointer displacement leaves the numeric value unchanged.");
        control.ContinueAdjustment(-76, fine: false);
        Require(level > fine && level - fine <= 1.5, "Coarse movement resumes from the current value after releasing Shift.");
        control.ContinueAdjustment(-10000, fine: false);
        Require(level == 12, "Numeric dragging clamps at the supported maximum.");
        control.ContinueAdjustment(-9997, fine: false);
        Require(level < 12 && level >= 11, "Reversing direction after reaching a limit responds immediately without accumulated overshoot.");
    }

    private void CheckSockets(GraphNodeView node, bool collapsed)
    {
        var sockets = Descendants<GraphWorkspace.Socket>(node).ToList();
        Require(sockets.Count == 3, $"The {(collapsed ? "collapsed" : "expanded")} EQ retains audio input/output and control input sockets.");
        var centers = sockets.ToDictionary(socket => socket, socket => socket.TranslatePoint(new Point(socket.ActualWidth / 2, socket.ActualHeight / 2), node));
        Require(sockets.All(socket => EffectivelyVisible(socket, node) && socket.Content is FrameworkElement shape
                && shape.Visibility == Visibility.Visible && shape.ActualWidth > 0 && shape.ActualHeight > 0
                && Math.Abs(centers[socket].X - (socket.Output ? node.ActualWidth : 0)) <= 1
                && centers[socket].Y > 0 && centers[socket].Y < node.ActualHeight),
            $"All {(collapsed ? "collapsed" : "expanded")} EQ socket glyphs remain visible at the correct node edge, with centers inside its height.");
        var inputs = sockets.Where(socket => !socket.Output).OrderBy(socket => centers[socket].Y).ToList();
        double firstGlyphHeight = ((FrameworkElement)inputs[0].Content).ActualHeight;
        double secondGlyphHeight = ((FrameworkElement)inputs[1].Content).ActualHeight;
        Require(centers[inputs[1]].Y - centers[inputs[0]].Y >= (firstGlyphHeight + secondGlyphHeight) / 2 + 1,
            $"The {(collapsed ? "collapsed" : "expanded")} EQ audio/control input glyphs have separate, non-overlapping vertical positions; pointer hit-testing is not exercised.");
        Require(centers[inputs[1]].Y - centers[inputs[0]].Y >= (inputs[0].ActualHeight + inputs[1].ActualHeight) / 2 - .01,
            $"The {(collapsed ? "collapsed" : "expanded")} EQ audio/control button hit regions do not overlap after layout rounding.");
        _socketLayouts.Add(new
        {
            node = "Equalizer", collapsed, width = node.ActualWidth, height = node.ActualHeight,
            sockets = sockets.Select(socket => new { socket.Kind, socket.Output, x = centers[socket].X, y = centers[socket].Y,
                targetWidth = socket.ActualWidth, targetHeight = socket.ActualHeight,
                glyphWidth = ((FrameworkElement)socket.Content).ActualWidth, glyphHeight = ((FrameworkElement)socket.Content).ActualHeight }).ToArray()
        });
    }

    private static bool EffectivelyVisible(FrameworkElement element, DependencyObject boundary)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement visual && visual.Visibility != Visibility.Visible) return false;
            if (ReferenceEquals(current, boundary)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static bool SameColour(Brush? actual, Brush expected) => actual is SolidColorBrush first
        && expected is SolidColorBrush second && first.Color == second.Color;

    public void WriteFailure(Exception error) => WriteReport(false, error.ToString());

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); ++index)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private void WriteReport(bool passed, string? error)
    {
        File.WriteAllText(Path.Combine(_directory, "results.json"), JsonSerializer.Serialize(new
        {
            passed, mode = "offscreen-design-render", checks = _checks, images = _images, imageSizes = _imageSizes, dpiEvidence = _dpiEvidence, socketLayouts = _socketLayouts,
            zoomEvidence = _zoomEvidence, windowLayouts = _windowLayouts,
            bindingErrors = _bindings.Messages, error,
            scope = "Detached WPF layout and binding checks using in-memory fixtures. No desktop control, shown window, audio engine, live audio, saved settings, or hardware interaction. This does not verify real mouse/keyboard behavior or audio reliability.",
            workspaceWidth = Width, workspaceHeight = Height, imageDpiValues = new[] { 96, 144, 192 }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed class BindingListener : TraceListener
    {
        private readonly List<string> _messages = [];
        public string[] Messages { get { lock (_messages) return _messages.ToArray(); } }
        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) lock (_messages) _messages.Add(message);
        }
        public override void WriteLine(string? message) => Write(message);
    }
}
