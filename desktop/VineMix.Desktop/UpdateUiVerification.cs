using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using VineMix.Desktop.Controls;
using VineMix.UpdateCore;

namespace VineMix.Desktop;

/// <summary>
/// Actual update content and editor routing, entirely detached. The preview
/// controller rejects network checks before constructing a remote source.
/// Native focus traversal and real download/install operations are excluded.
/// </summary>
internal static class UpdateUiVerification
{
    internal static async Task RunAsync(Action<bool, string> require, Action<FrameworkElement, string>? render = null)
    {
        IInputElement? focus = Keyboard.FocusedElement;
        using var controller = new UpdateController(preview: true);
        using var preferences = new MixerViewModel(preview: true);
        var changed = new HashSet<string>();
        controller.PropertyChanged += (_, args) => { if (args.PropertyName is { } name) changed.Add(name); };
        int closed = 0, installed = 0;
        var content = MainWindow.CreateUpdateContent(controller, () => closed++, () => { installed++; return Task.CompletedTask; }, preferences);
        var root = new Border { Width = 500, Child = content, UseLayoutRounding = true };
        root.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        root.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
        var buttons = Descendants<Button>(content).ToDictionary(button => (string)button.Content);
        var status = Descendants<TextBlock>(content).Single(block => AutomationProperties.GetName(block) == "Update status");
        var progress = Descendants<ProgressBar>(content).Single();
        var notes = Descendants<TextBox>(content).Single(box => AutomationProperties.GetName(box) == "Release notes");
        var notesSection = Descendants<StackPanel>(content).Single(panel => AutomationProperties.GetName(panel) == "Release details");
        var automatic = Descendants<CheckBox>(content).Single();
        await Arrange(root);
        Capture("updates-idle.png");
        require(buttons.Keys.ToHashSet().SetEquals(["Check for updates", "Download update", "Install and restart", "Cancel", "Close"])
            && Descendants<TextBlock>(content).Any(block => block.Text == "Installed · " + controller.CurrentVersion)
            && controller.CurrentVersion == ReleaseInfo.DisplayVersion,
            "The minimal update surface identifies the installed release and retains check, download, install, cancel and close actions.");
        foreach (var binding in new (string Label, string Property)[]
        {
            ("Check for updates", nameof(UpdateController.CanCheck)), ("Download update", nameof(UpdateController.CanDownload)),
            ("Install and restart", nameof(UpdateController.CanInstall)), ("Cancel", nameof(UpdateController.CanCancel))
        })
        {
            var expression = buttons[binding.Label].GetBindingExpression(UIElement.IsEnabledProperty);
            require(expression?.ParentBinding.Path.Path == binding.Property && buttons[binding.Label].Focusable && buttons[binding.Label].IsTabStop,
                binding.Label + " follows the controller's actual availability binding and retains normal keyboard navigation properties.");
        }
        RequireIdle();
        bool initialAutomatic = preferences.AutomaticallyCheckForUpdates;
        automatic.IsChecked = !initialAutomatic; await Flush();
        require(preferences.AutomaticallyCheckForUpdates == !initialAutomatic
            && AutomationProperties.GetHelpText(automatic).Contains("You choose when to download and install", StringComparison.Ordinal),
            "The automatic-check checkbox changes the actual preference in the inert model and clearly distinguishes checking from installing.");
        preferences.AutomaticallyCheckForUpdates = initialAutomatic; await Flush();
        require(automatic.IsChecked == initialAutomatic, "An external preference change also updates the dialog checkbox through its two-way binding.");
        require(AutomationProperties.GetLiveSetting(status) == AutomationLiveSetting.Polite
            && notes.IsReadOnly && notes.TextWrapping == TextWrapping.Wrap && notes.MaxHeight == 180
            && notes.VerticalScrollBarVisibility == ScrollBarVisibility.Auto
            && AutomationProperties.GetHelpText(buttons["Install and restart"]).Contains("stop audio", StringComparison.Ordinal),
            "Update status is an accessible polite live region; release notes are readable and scrollable, and installation explains the audio interruption.");
        buttons["Check for updates"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Flush();
        require(controller.Message.Contains("Preview only", StringComparison.Ordinal) && !controller.IsBusy
            && status.Text == controller.Message && controller.Download is null && controller.Notes.Length == 0,
            "The actual Check action uses the preview guard, reports that GitHub was not contacted, and cannot create an update offer or download.");
        string denied = controller.Message;
        await controller.DownloadAsync(); controller.Cancel();
        require(controller.Message == denied && controller.Download is null && !controller.CanInstall && installed == 0,
            "Without a verified offer the preview cannot download or install, and idle cancellation performs no operation.");

        controller.SetInstalling(true, "Preparing the verified update. Audio will stop briefly."); await Arrange(root);
        require(controller.IsBusy && controller.IsInstalling && !controller.CanCheck && !controller.CanDownload
            && !controller.CanInstall && !controller.CanCancel && progress.Visibility == Visibility.Visible && progress.IsIndeterminate
            && new[] { "Check for updates", "Download update", "Install and restart", "Cancel" }.All(label => !buttons[label].IsEnabled && buttons[label].Visibility == Visibility.Collapsed)
            && !buttons["Close"].IsEnabled,
            "Installation hides unavailable actions, disables Close and keeps the progress indicator visible.");
        Capture("updates-installing.png");
        buttons["Close"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        controller.Cancel(); await controller.CheckAsync();
        require(closed == 0 && controller.IsInstalling && controller.Message.Contains("Preparing", StringComparison.Ordinal),
            "Close, cancellation and another check cannot dismiss or replace an installation already in progress.");
        controller.SetInstalling(false); controller.Report("The update did not finish. Your current session is still available; try again when ready.");
        await Arrange(root); RequireIdle();
        Capture("updates-error.png");
        require(status.Text == controller.Message && changed.IsSupersetOf([nameof(UpdateController.Message), nameof(UpdateController.IsBusy),
            nameof(UpdateController.IsInstalling), nameof(UpdateController.CanCheck), nameof(UpdateController.CanDownload),
            nameof(UpdateController.CanInstall), nameof(UpdateController.CanCancel), nameof(UpdateController.Notes)]),
            "An installation refusal reaches the actual status binding and restores availability through the controller's property notifications.");

        var offer = CreateOffer();
        controller.SetPreviewState(offer); controller.Report($"VineMix {offer.Version} is available."); await Arrange(root);
        require(notesSection.Visibility == Visibility.Visible && notes.Text == offer.Manifest.Notes
            && buttons["Download update"].Visibility == Visibility.Visible && buttons["Install and restart"].Visibility == Visibility.Collapsed
            && buttons["Cancel"].Visibility == Visibility.Collapsed,
            "A verified offer reveals release notes and the download action without showing later-stage installation or cancellation.");
        Capture("updates-available.png");
        controller.SetPreviewState(offer, downloading: true);
        controller.Report("Downloading and verifying the update… You can keep using your audio."); await Arrange(root);
        require(progress.Visibility == Visibility.Visible && buttons["Cancel"].Visibility == Visibility.Visible
            && buttons["Close"].IsEnabled && buttons["Download update"].Visibility == Visibility.Collapsed
            && buttons["Check for updates"].Visibility == Visibility.Collapsed,
            "An active download shows its cancellation action and hides competing check and download actions.");
        Capture("updates-downloading.png");
        var download = new DownloadedUpdate("preview-only.exe", "preview-only", offer.Manifest.Installer, offer);
        controller.SetPreviewState(offer, download);
        controller.Report($"VineMix {offer.Version} is ready. Install and restart will briefly stop audio, then reopen your saved workspace.");
        await Arrange(root);
        require(buttons["Install and restart"].Visibility == Visibility.Visible && buttons["Download update"].Visibility == Visibility.Collapsed
            && !controller.CanInstall && !controller.CanDownload && progress.Visibility == Visibility.Collapsed,
            "A prepared update replaces download with install, while detached fixtures still cannot run a download or installation.");
        Capture("updates-ready.png");
        foreach (double dpi in new[] { 96d, 144d, 192d })
        foreach (double width in new[] { 440d, 500d })
        {
            VisualTreeHelper.SetRootDpi(root, new DpiScale(dpi / 96, dpi / 96));
            root.Width = width; await Arrange(root);
            bool Fits(FrameworkElement element)
            {
                var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
                return bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0
                    && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1;
            }
            require(buttons.Values.Where(button => button.Visibility == Visibility.Visible).All(Fits)
                && Fits(status) && Fits(notes) && Fits(automatic) && status.TextWrapping == TextWrapping.Wrap
                && status.ActualHeight + status.Margin.Top + status.Margin.Bottom >= status.DesiredSize.Height - .5,
                $"At {width:0} device-independent pixels and {dpi:0} DPI all visible update actions, notes, status and preferences fit their measured bounds.");
        }
        VisualTreeHelper.SetRootDpi(root, new DpiScale(1, 1)); root.Width = 500;
        bool? previousContrast = ConsoleTheme.PreviewHighContrast;
        // Application resource invalidation visits its Window trees. Keep the
        // fixture in a logical window, as in production, without ever showing
        // it or constructing an HWND; only its child is measured/rendered.
        var themeHost = new Window { Content = root };
        try
        {
            ConsoleTheme.SetPreviewHighContrast(true); await Arrange(root);
            Capture("updates-high-contrast.png");
            var actionBrush = buttons["Install and restart"].Background;
            var textBrush = ConsoleTheme.Get("ConsoleTextBrush");
            var primaryBrush = ConsoleTheme.Get("BrandActionBrush");
            require(SameColour(status.Foreground, textBrush) && SameColour(actionBrush, primaryBrush),
                $"The redesigned update surface follows the shared Windows high-contrast palette dynamically (text {status.Foreground}/{textBrush}, action {actionBrush}/{primaryBrush}).");
            require(!themeHost.IsVisible && new System.Windows.Interop.WindowInteropHelper(themeHost).Handle == IntPtr.Zero,
                "The high-contrast fixture uses only logical window ownership and never creates a native window.");
        }
        finally { ConsoleTheme.SetPreviewHighContrast(previousContrast); themeHost.Content = null; themeHost.Close(); }
        buttons["Close"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(closed == 1 && installed == 0 && !controller.IsInstalling && PresentationSource.FromVisual(root) is null,
            "Close invokes its injected callback only after installation ends, without invoking installation or creating a native dialog.");

        VerifyEditorCommit(require);
        require(ReferenceEquals(Keyboard.FocusedElement, focus) && PresentationSource.FromVisual(root) is null,
            "Update content and editor verification leave native keyboard focus unchanged and never open a desktop window.");

        void RequireIdle() => require(controller.CanCheck && !controller.CanDownload && !controller.CanInstall && !controller.CanCancel
            && buttons["Check for updates"].IsEnabled && !buttons["Download update"].IsEnabled
            && !buttons["Install and restart"].IsEnabled && !buttons["Cancel"].IsEnabled
            && buttons["Close"].IsEnabled && progress.Visibility == Visibility.Collapsed
            && notesSection.Visibility == Visibility.Collapsed
            && buttons.Values.Where(button => button.Visibility == Visibility.Visible).Select(button => (string)button.Content)
                .ToHashSet().SetEquals(["Close", "Check for updates"]),
            "Idle updates show only checking and Close, with no empty release-notes area or disabled future actions.");

        void Capture(string name)
        {
            root.Height = Math.Ceiling(root.ActualHeight);
            render?.Invoke(root, name);
            root.Height = double.NaN;
        }
    }

    private static bool SameColour(Brush actual, Brush expected) => actual is SolidColorBrush first
        && expected is SolidColorBrush second && first.Color == second.Color;

    private static UpdateOffer CreateOffer()
    {
        // An ephemeral signing key and clearly labelled notes exercise the real
        // view with a structurally verified offer. Nothing is fetched or saved.
        const string version = "0.3.0-beta.5";
        UpdateAsset Asset(string kind)
        {
            string name = $"VineMix-{version}-{kind}-x64.exe";
            return new() { FileName = name, Url = UpdateTrust.AssetUrl(version, name), Size = 1, Sha256 = new string('0', 64) };
        }
        var manifest = new UpdateManifest { Version = version, NumericVersion = "0.3.0.6",
            Notes = "Example release notes for this detached preview.\n\nA quieter update window, clearer automatic checks and reliability improvements.\n\nYour workspace and audio settings are preserved.",
            Installer = Asset("Setup"), Standalone = Asset("Standalone") };
        using var key = RSA.Create(3072);
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, options);
        byte[] envelope = JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, keyId = "detached-ui", algorithm = UpdateTrust.Algorithm,
            payload = Convert.ToBase64String(payload), signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) });
        return new UpdateTrust(new Dictionary<string, string> { ["detached-ui"] = key.ExportSubjectPublicKeyInfoPem() })
            .Verify(envelope, version, new UpdateCompatibility { WindowsBuild = 22000, Architecture = "x64" });
    }

    private static void VerifyEditorCommit(Action<bool, string> require)
    {
        var edits = new List<double>();
        var dial = new RotaryValueControl("Level", -60, 12, .1, "dB", 0, edits.Add);
        var entry = Descendants<TextBox>(dial).Single();
        var surface = new Border { Width = 420, Child = dial };
        surface.Measure(new Size(420, double.PositiveInfinity));
        surface.Arrange(new Rect(new Point(), surface.DesiredSize)); surface.UpdateLayout();
        entry.Text = "invalid draft";
        require(dial.HandleEntryKey(Key.Tab, ModifierKeys.None),
            "The real rotary Tab validator marks an invalid draft before testing update-editor routing without native focus.");
        var inner = Descendants<FrameworkElement>(entry).FirstOrDefault() ?? entry;
        require(!UpdateEditorCommit.TryCommit(inner) && entry.Text == "invalid draft" && dial.Value == 0 && edits.Count == 0
            && AutomationProperties.GetItemStatus(entry).Length > 0,
            "The actual update-editor ancestor route refuses an invalid rotary draft, preserves its text and accessibility error, and emits no audio edit.");
        entry.Text = (-7.125).ToString("G", CultureInfo.CurrentCulture) + " dB";
        require(UpdateEditorCommit.TryCommit(entry) && dial.Value == -7.125 && edits.SequenceEqual([-7.125])
            && AutomationProperties.GetItemStatus(entry).Length == 0,
            "Correcting the retained draft lets the same update commit route apply one precise value and clear its validation error.");
        require(UpdateEditorCommit.TryCommit(entry) && edits.Count == 1,
            "Rechecking an already committed rotary entry does not emit a second audio edit.");

        var model = new NumericDraft();
        var text = new TextBox();
        var binding = new Binding(nameof(NumericDraft.Value))
            { Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.Explicit, ValidatesOnExceptions = true };
        binding.ValidationRules.Add(new NumberRule());
        text.SetBinding(TextBox.TextProperty, binding);
        text.Text = "not a number";
        require(!UpdateEditorCommit.TryCommit(text) && Validation.GetHasError(text) && text.Text == "not a number" && model.Value == 4,
            "An ordinary bound numeric editor also retains an invalid draft and rejects update preparation without changing its source.");
        text.Text = (3.25).ToString(CultureInfo.CurrentCulture);
        require(UpdateEditorCommit.TryCommit(text) && !Validation.GetHasError(text) && model.Value == 3.25
            && UpdateEditorCommit.TryCommit(null),
            "A corrected bound numeric draft is explicitly committed before updating, and a missing focused editor is harmless.");
    }

    private static async Task Arrange(FrameworkElement root)
    {
        await Flush(); root.InvalidateMeasure(); root.Measure(new Size(root.Width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, root.Width, root.DesiredSize.Height)); root.UpdateLayout(); await Flush();
    }
    private static async Task Flush() => await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private sealed class NumericDraft { public double Value { get; set; } = 4; }
    private sealed class NumberRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
            => double.TryParse(value as string, NumberStyles.Float, cultureInfo, out double number) && double.IsFinite(number)
                ? ValidationResult.ValidResult : new ValidationResult(false, "Enter a finite number.");
    }
}
