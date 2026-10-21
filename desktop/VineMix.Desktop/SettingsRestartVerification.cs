using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

/// <summary>Detached Settings routing through an inert, gated engine lifecycle.</summary>
internal static class SettingsRestartVerification
{
    private static readonly TimeSpan CompletionDeadline = TimeSpan.FromSeconds(5);

    internal static async Task RunAsync(Action<bool, string> require)
    {
        ConsoleTheme.Initialize();
        await VerifySuccessAsync(require);
        await VerifyOfflineFailureAsync(require);
    }

    private static async Task VerifySuccessAsync(Action<bool, string> require)
    {
        var engine = new FakeEngine();
        using var mixer = new MixerViewModel(engine.Operations);
        mixer.ApplyPreviewSnapshot(engine.Snapshot());
        var feedback = new Feedback();
        var menu = MainWindow.CreateSettingsMenu(mixer, feedback.Receive);
        var restart = Item(menu, "Restart audio engine");
        require(mixer.IsPreview && mixer.CanEdit && !menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "Restart menu verification uses an editable preview and detached ContextMenu with no popup, process, pipe or personal settings.");
        var enabled = restart.GetBindingExpression(UIElement.IsEnabledProperty);
        require(enabled is not null && enabled.ParentBinding.Path.Path == nameof(MixerViewModel.CanRestartEngine)
            && ReferenceEquals(enabled.ParentBinding.Source, mixer),
            "Settings restart availability follows the model's restart state, independently of ordinary editing availability.");
        VerifyPreferences(menu, mixer, require);
        require(Item(menu, "Windows sound settings") is not null && Item(menu, "Retry assigned devices") is not null,
            "Adding restart preserves the existing Windows sound settings and assigned-device retry entries without invoking either external action.");
        mixer.Edit(configuration => configuration.Graph.Nodes.Single().GainDb = -12);
        require(mixer.PendingEditCount > 0 && mixer.CanUndo,
            "The restart menu scenario begins with a real queued preview edit and an existing undo step to preserve.");
        string graphBefore = JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json);
        var preferencesBefore = (mixer.RightClickDragToPan, mixer.ReducedAnimation, mixer.StartAtLogin);
        int historyBefore = mixer.EditHistory.Entries.Count;
        bool dispatched = false;
        try
        {
            // Invoke only the routed handler on a detached item. Never open the
            // menu, create a window, focus a control or send desktop input.
            dispatched = true;
            restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await engine.RestartEntered.Task.WaitAsync(CompletionDeadline);
            enabled!.UpdateTarget();
            require(mixer.IsRestartingEngine && !mixer.CanRestartEngine && !restart.IsEnabled && !mixer.CanEdit,
                "While the fake restart is held in flight, Settings disables its restart action and the model suspends graph editing.");
            require(engine.RestartCalls == 1 && feedback.Messages.Count == 1
                && !feedback.Messages[0].Error && feedback.Messages[0].Message.Contains("Restarting", StringComparison.OrdinalIgnoreCase),
                "The menu reports progress immediately and dispatches exactly one restart before lifecycle completion.");

            // Raising a routed event deliberately bypasses normal disabled-item
            // input suppression, so this exercises the handler's own busy guard.
            restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            require(engine.RestartCalls == 1 && feedback.Messages.Count == 1,
                "A duplicate routed click during restart cannot launch another lifecycle operation or duplicate its progress feedback.");
            engine.ReleaseRestart.TrySetResult();
            await feedback.Completed.Task.WaitAsync(CompletionDeadline);
            enabled.UpdateTarget();
            require(feedback.Messages.Count == 2 && !feedback.Messages[1].Error
                && feedback.Messages[1].Message.Contains("restarted", StringComparison.OrdinalIgnoreCase),
                "Successful engine recovery produces one final success message through the actual Settings feedback callback.");
            require(!mixer.IsRestartingEngine && mixer.CanRestartEngine && restart.IsEnabled && mixer.CanEdit,
                "After successful recovery, Settings permits another restart and ordinary editing becomes available again.");
            require(JsonSerializer.Serialize(mixer.GraphConfig, Protocol.Json) == graphBefore
                && (mixer.RightClickDragToPan, mixer.ReducedAnimation, mixer.StartAtLogin) == preferencesBefore
                && mixer.EditHistory.Entries.Count == historyBefore && mixer.PendingEditCount == 0,
                "The Settings restart preserves the workspace, existing preferences and undo history while finishing queued saves.");
            require(engine.Requests.All(command => command is "snapshot" or "configure") && engine.ResetCalls > 0,
                "Menu recovery uses only the injected request and connection-reset delegates; no real engine or control endpoint is involved.");
        }
        finally
        {
            engine.ReleaseRestart.TrySetResult();
            if (dispatched) await feedback.Completed.Task.WaitAsync(CompletionDeadline);
        }
        require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "The success scenario completes without opening its Settings popup or touching a native window.");
    }

    private static async Task VerifyOfflineFailureAsync(Action<bool, string> require)
    {
        const string failure = "Detached restart failure marker";
        var engine = new FakeEngine { Failure = failure };
        using var mixer = new MixerViewModel(engine.Operations);
        var feedback = new Feedback();
        var menu = MainWindow.CreateSettingsMenu(mixer, feedback.Receive);
        var restart = Item(menu, "Restart audio engine");
        var retry = Item(menu, "Retry assigned devices");
        var startup = Preference(menu, nameof(MixerViewModel.StartAtLogin));
        restart.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();
        retry.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();
        startup.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();
        require(mixer.IsPreview && !mixer.CanEdit && mixer.CanRestartEngine && restart.IsEnabled
            && !retry.IsEnabled && !startup.IsEnabled,
            "An offline engine leaves Settings restart available while normal device retry and sign-in configuration remain unavailable.");
        bool dispatched = false;
        try
        {
            dispatched = true;
            restart.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await engine.RestartEntered.Task.WaitAsync(CompletionDeadline);
            restart.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();
            require(!restart.IsEnabled && engine.RestartCalls == 1 && feedback.Messages.Count == 1 && !feedback.Messages[0].Error,
                "An offline restart still enters the same disabled busy state and reports progress before the fake lifecycle completes.");
            engine.ReleaseRestart.TrySetResult();
            await feedback.Completed.Task.WaitAsync(CompletionDeadline);
            restart.GetBindingExpression(UIElement.IsEnabledProperty)!.UpdateTarget();
            require(feedback.Messages.Count == 2 && feedback.Messages[1].Error
                && feedback.Messages[1].Message.Contains(failure, StringComparison.Ordinal),
                "A lifecycle failure reaches the menu's final feedback callback as an error with its actionable failure detail.");
            require(!mixer.IsRestartingEngine && mixer.CanRestartEngine && restart.IsEnabled && engine.RestartCalls == 1,
                "A failed restart releases the busy guard and allows a deliberate retry without an automatic duplicate dispatch.");
        }
        finally
        {
            engine.ReleaseRestart.TrySetResult();
            if (dispatched) await feedback.Completed.Task.WaitAsync(CompletionDeadline);
        }
        require(!menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "The offline failure scenario also remains entirely detached from the desktop.");
    }

    private static void VerifyPreferences(ContextMenu menu, MixerViewModel mixer, Action<bool, string> require)
    {
        foreach (var preference in new (string Property, Func<bool> Read, Action<bool> Write)[]
        {
            (nameof(MixerViewModel.RightClickDragToPan), () => mixer.RightClickDragToPan, value => mixer.RightClickDragToPan = value),
            (nameof(MixerViewModel.ReducedAnimation), () => mixer.ReducedAnimation, value => mixer.ReducedAnimation = value),
            (nameof(MixerViewModel.StartAtLogin), () => mixer.StartAtLogin, value => mixer.StartAtLogin = value)
        })
        {
            var toggle = Preference(menu, preference.Property);
            var binding = toggle.GetBindingExpression(MenuItem.IsCheckedProperty)!;
            bool original = preference.Read();
            try
            {
                binding.UpdateTarget();
                require(toggle.IsCheckable && toggle.StaysOpenOnClick && toggle.IsChecked == original
                    && binding.ParentBinding.Mode == BindingMode.TwoWay,
                    $"Settings retains the existing checked, two-way {preference.Property} preference alongside restart.");
                toggle.SetCurrentValue(MenuItem.IsCheckedProperty, !original);
                binding.UpdateSource();
                require(preference.Read() == !original,
                    $"The existing {preference.Property} menu binding still changes its preview preference.");
            }
            finally
            {
                preference.Write(original);
                binding.UpdateTarget();
            }
        }
    }

    private static MenuItem Item(ContextMenu menu, string header)
        => menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));
    private static MenuItem Preference(ContextMenu menu, string property)
        => menu.Items.OfType<MenuItem>().Single(item =>
            item.GetBindingExpression(MenuItem.IsCheckedProperty)?.ParentBinding.Path.Path == property);

    private sealed class Feedback
    {
        internal readonly List<(string Message, bool Error)> Messages = [];
        internal readonly TaskCompletionSource Completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Receive(string message, bool error)
        {
            Messages.Add((message, error));
            if (Messages.Count >= 2) Completed.TrySetResult();
        }
    }

    private sealed class FakeEngine
    {
        private MixerConfiguration _configuration;
        private ulong _frames = 100;
        internal readonly TaskCompletionSource RestartEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseRestart = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<string> Requests = [];
        internal int RestartCalls, ResetCalls;
        internal string Failure { get; init; } = "";
        internal EngineRestartOperations Operations => new(RequestAsync, RestartAsync, () => ResetCalls++)
        {
            IsRunning = () => true,
            RetryDelay = TimeSpan.Zero,
            Deadline = TimeSpan.FromSeconds(10)
        };

        internal FakeEngine()
        {
            var gain = GraphDefaults.NewNode("gain", 120, 80);
            gain.Id = "detached-settings-restart"; gain.GainDb = -7;
            _configuration = new MixerConfiguration
            {
                Version = 2, Revision = 7,
                Graph = new() { Nodes = [gain], PanX = 24, PanY = -16, Zoom = .8 }
            };
        }

        internal MixerSnapshot Snapshot() => new()
        {
            Configuration = _configuration.Copy(),
            Status = new() { AudioFrames = ++_frames },
            SettingsPath = "detached-settings-restart-no-file"
        };

        private Task<ProtocolResponse> RequestAsync(string command, MixerConfiguration? configuration, ulong? revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(command);
            if (command == "configure")
            {
                if (configuration is null || revision != _configuration.Revision)
                    return Task.FromResult(new ProtocolResponse { Ok = false, Error = "Detached fixture revision mismatch." });
                _configuration = configuration.Copy(); _configuration.Revision++;
            }
            else if (command != "snapshot") throw new InvalidOperationException("Unexpected command in detached restart fixture: " + command);
            return Task.FromResult(new ProtocolResponse { Ok = true, Snapshot = Snapshot() });
        }

        private async Task RestartAsync(CancellationToken token)
        {
            RestartCalls++;
            RestartEntered.TrySetResult();
            await ReleaseRestart.Task.WaitAsync(token);
            if (Failure.Length > 0) throw new IOException(Failure);
            _frames = 0;
        }
    }
}
