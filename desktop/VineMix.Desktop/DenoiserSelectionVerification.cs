using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

internal static class DenoiserSelectionVerification
{
    internal static async Task RunAsync(GraphWorkspace workspace, MixerViewModel mixer, Func<Task> layout,
        Action<bool, string> require, Action<GraphNodeView, string> render)
    {
        VerifySavedDefaults(require);
        var node = GraphDefaults.NewNode("denoiser", 40, 40);
        var graph = new GraphConfiguration { Nodes = [node], Zoom = 1 };
        mixer.ApplyPreviewSnapshot(new() { Configuration = new() { Version = 2, Graph = graph }, Status = new() });
        await layout();
        GraphNode Current() => mixer.GraphConfig.Nodes.Single();
        GraphNodeView View() => Descendants<GraphNodeView>(workspace).Single();
        ComboBox Choice() => Descendants<ComboBox>(View()).Single(box => AutomationProperties.GetName(box) == "Denoiser algorithm");
        RotaryValueControl Dial(string label) => Descendants<RotaryValueControl>(View()).Single(control =>
            AutomationProperties.GetName(Descendants<Slider>(control).Single()) == label);
        string Json() => JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        require(Choice().Items.OfType<ComboBoxItem>().Select(item => item.Content?.ToString()).SequenceEqual(new[] { "SpeexDSP · 20 ms", "RNNoise · 30 ms" })
            && AutomationProperties.GetHelpText(Choice()).Contains("node delay including bypass, not total system latency", StringComparison.Ordinal),
            "Denoiser choices show both measured node delays with accessible scope and bypass wording, preserving saved algorithm tags.");
        require(Choice().ToolTip is StackPanel tip && tip.Children.OfType<TextBlock>().Select(line => line.Text).SequenceEqual(new[]
            { "Algorithm", "SpeexDSP: faster, lighter cleanup.", "RNNoise: more delay, usually cleaner speech.", "20/30 ms is added node delay." }),
            "Denoiser help briefly compares speed, cleanup and added delay without guaranteeing cleaner speech for every voice.");
        foreach (var item in Choice().Items.OfType<ComboBoxItem>())
        {
            var label = new FormattedText(item.Content.ToString(), System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface(Choice().FontFamily, Choice().FontStyle, Choice().FontWeight, Choice().FontStretch),
                Choice().FontSize, Brushes.White, 1);
            require(label.WidthIncludingTrailingWhitespace + 36 <= Choice().ActualWidth,
                "The complete denoiser delay label fits beside the dropdown arrow: " + item.Content);
        }
        require(Current().Denoiser.Algorithm == "rnnoise" && Current().Denoiser.RnnoiseMix == 1
            && Equals((Choice().SelectedItem as ComboBoxItem)?.Tag, "rnnoise")
            && Visible(Dial("Mix"), View()) && Dial("Mix").Value == 100 && !Visible(Dial("Reduction"), View()),
            "A newly added denoiser starts with RNNoise selected and its visible Mix at 100 percent.");
        // Loading an existing Speex node must not apply the new-node preference.
        node.Denoiser.Algorithm = "speex"; node.Denoiser.SuppressionDb = -27; node.Denoiser.RnnoiseMix = .65;
        mixer.ApplyPreviewSnapshot(new() { Configuration = new() { Version = 2, Graph = graph }, Status = new() });
        await layout();
        require(Current().Denoiser.Algorithm == "speex" && Visible(Dial("Reduction"), View()) && !Visible(Dial("Mix"), View()),
            "An existing Speex node still shows Reduction and hides Mix when loaded into the workspace.");
        var size = View().RenderSize;
        string original = Json(); long edits = mixer.ConfigurationPresentationCount;
        render(View(), "denoiser-speex-dropdown.png");
        Choice().SelectedItem = Choice().Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, "rnnoise"));
        await layout();
        require(Current().Denoiser.Algorithm == "rnnoise" && Current().Denoiser.SuppressionDb == -27 && Current().Denoiser.RnnoiseMix == .65
            && mixer.ConfigurationPresentationCount == edits + 1,
            "Selecting RNNoise records one graph edit and retains both algorithms' individual settings.");
        require(View().RenderSize == size && Visible(Dial("Mix"), View()) && !Visible(Dial("Reduction"), View()),
            "Switching algorithms keeps node geometry and ports stable while showing only the relevant dial.");
        require(Descendants<TextBlock>(View()).Any(text => text.Text == "Delay · 30 ms, including bypass"),
            "RNNoise displays its measured delay including bypass.");
        Descendants<Slider>(Dial("Mix")).Single().Value = 73;
        string edited = Json();
        require(Current().Denoiser.RnnoiseMix == .73 && Current().Denoiser.SuppressionDb == -27,
            "The neural Mix dial changes only the neural mix setting.");
        require(mixer.Undo() && Current().Denoiser.RnnoiseMix == .65 && mixer.Undo() && Json() == original,
            "Undo restores Mix and then the previous denoiser algorithm exactly.");
        require(mixer.Redo() && mixer.Redo() && Json() == edited,
            "Redo restores the algorithm and neural Mix without changing the retained spectral settings.");
        await layout(); render(View(), "denoiser-rnnoise-dropdown.png");
        foreach (double zoom in new[] { .5, .75, 1.5, 2d })
        {
            mixer.Edit(config => config.Graph.Zoom = zoom); await layout();
            require(View().RenderSize == size, "RNNoise selection retains complete node dimensions at " + zoom * 100 + "% zoom.");
        }
        string payload = GraphClipboard.Serialize(mixer.GraphConfig, [node.Id]);
        require(GraphClipboard.TryCreatePaste(payload, new(), new Point(), out var pasted, out _)
            && pasted.Nodes.Single().Denoiser.Algorithm == "rnnoise" && pasted.Nodes.Single().Denoiser.RnnoiseMix == .73
            && pasted.Nodes.Single().Denoiser.SuppressionDb == -27,
            "Clipboard copy/paste preserves the algorithm and both processors' settings.");
        var old = JsonNode.Parse(payload)!; var settings = old["nodes"]![0]!["denoiser"]!.AsObject();
        settings.Remove("algorithm"); settings.Remove("rnnoiseMix");
        require(GraphClipboard.TryCreatePaste(old.ToJsonString(), new(), new Point(), out var legacy, out _)
            && legacy.Nodes.Single().Denoiser.Algorithm == "speex" && legacy.Nodes.Single().Denoiser.SuppressionDb == -27
            && legacy.Nodes.Single().Denoiser.RnnoiseMix == 1,
            "Old clipboard denoisers default to Speex and preserve their original suppression.");
        foreach (var invalid in new[] { "unknown", "RNNoise", "", null })
        {
            var bad = JsonNode.Parse(payload)!; bad["nodes"]![0]!["denoiser"]!["algorithm"] = invalid;
            require(!GraphClipboard.TryCreatePaste(bad.ToJsonString(), new(), new Point(), out _, out _),
                "Unknown, mis-cased, empty and null algorithm values reject the clipboard group.");
        }
        foreach (double invalid in new[] { -.01, 1.01 })
        {
            var bad = JsonNode.Parse(payload)!; bad["nodes"]![0]!["denoiser"]!["rnnoiseMix"] = invalid;
            require(!GraphClipboard.TryCreatePaste(bad.ToJsonString(), new(), new Point(), out _, out _),
                "Out-of-range neural Mix rejects the clipboard group.");
        }
        require(!GraphClipboard.TryCreatePaste(payload.Replace("\"algorithm\":", "\"algorithm\":\"speex\",\"algorithm\":"), new(), new Point(), out _, out _),
            "Duplicate nested algorithm fields reject the clipboard group.");
        workspace.ResetNode(node.Id); await layout();
        require(Current().Denoiser.Algorithm == "rnnoise" && Current().Denoiser.SuppressionDb == -18 && Current().Denoiser.RnnoiseMix == 1,
            "Whole-node Reset restores both processors' controls while keeping the selected RNNoise algorithm.");
        require(mixer.Undo() && Current().Denoiser.Algorithm == "rnnoise" && Current().Denoiser.SuppressionDb == -27
            && Current().Denoiser.RnnoiseMix == .73 && mixer.Redo() && Current().Denoiser.RnnoiseMix == 1,
            "Undo and Redo restore denoiser control resets without switching the selected algorithm.");
        workspace.NodeChanged(node.Id, changed => { changed.Denoiser.Algorithm = "speex"; changed.Denoiser.SuppressionDb = -31; changed.Denoiser.RnnoiseMix = .42; });
        workspace.ResetNode(node.Id); await layout();
        require(Current().Denoiser.Algorithm == "speex" && Current().Denoiser.SuppressionDb == -18 && Current().Denoiser.RnnoiseMix == 1
            && Visible(Dial("Reduction"), View()) && !Visible(Dial("Mix"), View()),
            "Resetting an existing Speex node keeps Speex selected and restores Reduction without enabling RNNoise.");
        require(mixer.IsPreview && PresentationSource.FromVisual(workspace) is null,
            "Denoiser dropdown verification is detached, silent and independent of personal settings and audio devices.");
    }
    private static void VerifySavedDefaults(Action<bool, string> require)
    {
        var legacy = JsonSerializer.Deserialize<GraphNode>("""{"id":"old-denoiser","kind":"denoiser","denoiser":{"suppressionDb":-29}}""", Protocol.Json)!;
        var missing = JsonSerializer.Deserialize<GraphNode>("""{"id":"old-defaults","kind":"denoiser"}""", Protocol.Json)!;
        require(legacy.Denoiser.Algorithm == "speex" && legacy.Denoiser.SuppressionDb == -29 && legacy.Denoiser.RnnoiseMix == 1
            && missing.Denoiser.Algorithm == "speex" && missing.Denoiser.SuppressionDb == -18,
            "Saved denoisers without algorithm fields, or without the entire settings block, retain legacy Speex defaults.");
        require(GraphDefaults.NewNode("denoiser", 0, 0).Denoiser is { Algorithm: "rnnoise", RnnoiseMix: 1 }
            && GraphDefaults.NewNode("gain", 0, 0).Denoiser.Algorithm == "speex",
            "Only newly created denoiser nodes opt into RNNoise; unrelated node settings keep their existing defaults.");
        foreach (string algorithm in new[] { "speex", "rnnoise" })
        {
            var node = GraphDefaults.NewNode("denoiser", 0, 0);
            node.Denoiser.Algorithm = algorithm; node.Denoiser.SuppressionDb = -26; node.Denoiser.RnnoiseMix = .61;
            var graph = new GraphConfiguration { Nodes = [node] };
            var loaded = JsonSerializer.Deserialize<GraphConfiguration>(JsonSerializer.Serialize(graph, Protocol.Json), Protocol.Json)!;
            var copied = loaded.Copy(); copied.Nodes.Single().GainDb = -3;
            require(copied.Nodes.Single().Denoiser is { SuppressionDb: -26, RnnoiseMix: .61 } retained && retained.Algorithm == algorithm
                && loaded.Nodes.Single().Denoiser.Algorithm == algorithm,
                "Saved " + algorithm + " selection and processor settings survive loading, copying and an ordinary gain edit.");
            string payload = GraphClipboard.Serialize(graph, [node.Id]);
            require(GraphClipboard.TryCreatePaste(payload, new(), new Point(), out var pasted, out _)
                && pasted.Nodes.Single().Denoiser.Algorithm == algorithm && pasted.Nodes.Single().Denoiser.SuppressionDb == -26
                && pasted.Nodes.Single().Denoiser.RnnoiseMix == .61,
                "Pasting an existing " + algorithm + " denoiser preserves its chosen processor and controls instead of applying new-node defaults.");
        }
    }
    private static bool Visible(DependencyObject child, DependencyObject root)
    {
        for (DependencyObject? item = child; item is not null && item != root; item = VisualTreeHelper.GetParent(item))
            if (item is FrameworkElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }
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
