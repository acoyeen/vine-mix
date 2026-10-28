using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace VineMix.Desktop;

/// <summary>Inert checks against the actual delivered executable, without user data or HWNDs.</summary>
internal static class BetaReleaseSmoke
{
    internal static int Run(string directory)
    {
        string target = Path.GetFullPath(directory);
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("Choose a new smoke-test evidence directory.");
        Directory.CreateDirectory(target);
        try
        {
            string payload = EmbeddedRuntime.ValidatePackage();
            string? extractedEngine = EmbeddedRuntime.BuildMode == "Standalone"
                ? EmbeddedRuntime.ResolveEnginePath(Path.Combine(target,"runtime")) : null;
            using var model = new MixerViewModel(preview: true);
            model.ApplyPreviewSnapshot(new MixerSnapshot { Configuration = new MixerConfiguration { Version = 2, Graph = GraphDefaults.Fresh() } });
            var window = new MainWindow(model);
            var content = (FrameworkElement)window.Content; window.Content = null;
            content.Measure(new Size(1280,800)); content.Arrange(new Rect(0,0,1280,800)); content.UpdateLayout();
            var credits = CreditsContent.Create(() => { }, _ => { });
            credits.Measure(new Size(640, 580)); credits.Arrange(new Rect(0, 0, 640, 580)); credits.UpdateLayout();
            var notices = DistributionNotices.FileNames.Select(name => new { name, sha256 = DistributionNotices.Hash(name) }).ToArray();
            bool passed = model.GraphConfig.Nodes.Count == 3 && model.GraphConfig.Edges.Count == 2
                && model.GraphConfig.Nodes.Where(n => n.Kind is "microphone" or "output").All(n => !VirtualDeviceBindings.IsAssigned(n.Device))
                && DistributionNotices.ReadText("LICENSE").Contains("Copyright (c) 2026 acoyeen (VineMix, https://vinemix.app)", StringComparison.Ordinal)
                && DistributionNotices.ReadText("ATTRIBUTION.md").Contains("not additional MIT licence conditions", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(target,"results.json"), JsonSerializer.Serialize(new
            {
                passed, version = ReleaseInfo.Version, displayVersion = ReleaseInfo.DisplayVersion,
                mode = EmbeddedRuntime.BuildMode, payload, extractedEngine, notices,
                scope = "Delivered executable, metadata, runtime package integrity, embedded distribution notices, detached WPF starter and offline Credits layouts. No engine, personal settings, device enumeration or shown window."
            }, new JsonSerializerOptions { WriteIndented = true }));
            return passed ? 0 : 1;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(target,"results.json"), JsonSerializer.Serialize(new { passed = false, error = error.ToString() }));
            return 1;
        }
    }
}
