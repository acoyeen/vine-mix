using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal static class LinkedControlPresentationVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        var fixture = UiPreview.CreateSnapshot([]);
        var control = GraphDefaults.NewNode("control", 20, 20);
        var mix = GraphDefaults.NewNode("mix", 400, 20);
        var denoiser = GraphDefaults.NewNode("denoiser", 800, 20);
        var split = GraphDefaults.NewNode("stereoSplit", 1200, 20);
        fixture.Configuration.Graph = new() { Nodes = [control, mix, denoiser, split], Edges =
            [new() { Id = "linked-mix", From = control.Id, To = mix.Id, Kind = "control" }] };
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        GraphNode Current(string id) => mixer.GraphConfig.Nodes.Single(n => n.Id == id);
        GraphNodeView View(string id) => Descendants<GraphNodeView>(workspace).Single(v => v.Id == id);
        RotaryValueControl Multiplier() => Descendants<RotaryValueControl>(View(control.Id)).Single();
        Slider Range() => Descendants<Slider>(Multiplier()).Single();
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Linked-control presentation is tested in a detached synthetic workspace without audio or personal settings.");
        require(Range() is { Minimum: 0, Maximum: 200, Value: 100 }
            && AutomationProperties.GetName(Range()) == "Multiplier"
            && Multiplier().DefaultValue == 100 && Multiplier().NormalizedValue == .5,
            "Linked control has a percentage Multiplier with exact neutral midpoint and reset at 100%.");
        require(View(mix.Id).FindSocket("control", false) is { IsConnected: true }
            && View(denoiser.Id).FindSocket("control", false) is null
            && View(split.Id).FindSocket("control", false) is null
            && View(control.Id).FindSocket("control", false) is null,
            "Only the Level-bearing Mix has a normal control input; effects, channel routing and Linked control do not.");
        require(Descendants<TextBlock>((DependencyObject)Range().ToolTip).Select(t => t.Text).SequenceEqual(
            ["Multiplier", "Scales linked levels. 100% is unchanged; 0% is silent."]),
            "Multiplier hover help is one short explanation beneath its title.");
        render(View(control.Id), "linked-control-multiplier.png");
        render(View(denoiser.Id), "denoiser-no-level-control.png");
        render(View(split.Id), "stereo-split-no-level-control.png");
        var baseline = mixer.GraphConfig.Copy();
        Range().Value = 0; await layout();
        require(Current(control.Id) is { ControlMultiplier: 0, GainDb: 0, Muted: false }
            && View(control.Id).AvailabilityNoticeDetailTextElement.Text == "Linked levels silenced",
            "Zero percent is represented as a true zero multiplier, not a legacy mute or minimum decibel approximation.");
        var bypass = Descendants<Button>(View(control.Id)).Single(b => AutomationProperties.GetName(b) == "Bypass");
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
        require(Current(control.Id).Bypass && Current(control.Id).ControlMultiplier == 0
            && View(control.Id).AvailabilityNoticeDetailTextElement.Text == "Multiplier bypassed"
            && Equals(bypass.ToolTip, "Apply the multiplier again."),
            "Bypass keeps the saved zero multiplier while describing the controller accurately, without claiming audio passes through it.");
        bypass.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Range().Value = 200; await layout();
        var expected = baseline.Copy(); expected.Nodes.Single(n => n.Id == control.Id).ControlMultiplier = 2;
        require(Json(mixer.GraphConfig) == Json(expected), "Editing the multiplier changes only the controller and retains linked destinations and individual levels.");
        Multiplier().ResetToDefault(); await layout();
        require(Json(mixer.GraphConfig) == Json(baseline) && Range().Value == 100,
            "Multiplier reset restores exactly 100% without adjusting target levels or connections.");
        require(workspace.ConnectNodes(control.Id, denoiser.Id, "control").Length > 0
            && workspace.ConnectNodes(control.Id, split.Id, "control").Length > 0,
            "New control links cannot be created to effect or channel-routing nodes that have no Level knob.");

        fixture.Configuration.Graph.Edges.Add(new() { Id = "saved-effect-link", From = control.Id, To = denoiser.Id, Kind = "control" });
        string savedGraph = Json(fixture.Configuration.Graph);
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(View(denoiser.Id).FindSocket("control", false) is { IsConnected: true }
            && Json(mixer.GraphConfig) == savedGraph,
            "An older saved effect link remains visible and unchanged instead of silently losing its level adjustment.");
        mixer.Edit(c => c.Graph.Edges.RemoveAll(e => e.Id == "saved-effect-link")); await layout();
        require(View(denoiser.Id).FindSocket("control", false) is null,
            "Removing an old effect link also removes its compatibility-only socket.");
        require(mixer.Undo(), "Removing an old level link can be undone."); await layout();
        require(View(denoiser.Id).FindSocket("control", false) is { IsConnected: true },
            "Undo restores the saved effect link and its visible socket together.");

        control.ControlMultiplier = -1; control.GainDb = 12;
        string legacyGraph = Json(fixture.Configuration.Graph);
        mixer.ApplyPreviewSnapshot(fixture); await layout();
        require(!Descendants<Slider>(View(control.Id)).Any()
            && Descendants<TextBlock>(View(control.Id)).Any(t => t.Text.StartsWith("Saved multiplier 398.", StringComparison.Ordinal))
            && Json(mixer.GraphConfig) == legacyGraph,
            "An older controller above 200% displays its true saved value without silently clamping or changing its sound.");
        render(View(control.Id), "linked-control-legacy-value.png");
        var convert = Descendants<Button>(View(control.Id)).Single(b => AutomationProperties.GetName(b) == "Use multiplier");
        convert.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await layout();
        require(Current(control.Id) is { ControlMultiplier: 1, GainDb: 0 } && Range().Value == 100,
            "An explicit Use multiplier action switches a legacy value to the normal 100% control.");
        require(mixer.Undo(), "Legacy multiplier conversion can be undone."); await layout();
        require(Json(mixer.GraphConfig) == legacyGraph && !Descendants<Slider>(View(control.Id)).Any(),
            "Undo restores the precise legacy gain and its accurate saved-value presentation.");
    }

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, Protocol.Json);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
