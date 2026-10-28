using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace VineMix.Desktop;

// In-memory requests and detached WPF controls; recovery file checks use a
// unique temporary folder, never the user's workspace or personal audio engine.
internal static class QuitRecoveryVerification
{
    internal static async Task RunAsync(Action<bool, string> require)
    {
        await SaveBeforeTeardown(require);
        await FailedSaveRetryCancelAndCopy(require);
        await FailedPreferencesSave(require);
        await LostReplyAndCancelledReply(require);
        await ReplayConflict(require);
        await FailedUpdateHealthQuit(require);
        await FlowChoices(require);
        await SettingsAndDialog(require);
    }

    internal static FrameworkElement Preview()
    {
        var surface = new Border { Width = 540,
            Child = MainWindow.CreateQuitRecoveryContent(new(false, "Example: the workspace folder is read-only. Your queued changes are still in this window.", 2), _ => { }) };
        surface.SetResourceReference(Border.BackgroundProperty, "ConsoleWindowBrush");
        surface.SetResourceReference(TextBlock.ForegroundProperty, "ConsoleTextBrush");
        surface.Measure(new Size(540, double.PositiveInfinity));
        surface.Height = Math.Ceiling(surface.DesiredSize.Height);
        return surface;
    }

    private static async Task SaveBeforeTeardown(Action<bool, string> require)
    {
        var engine = new Engine { HoldConfigure = Signal() };
        using var mixer = Connected(engine);
        mixer.Edit(c => c.Graph.Nodes[0].GainDb = -9);
        mixer.SetCanvasView(100, 80, .8);
        var external = GraphDefaults.NewNode("eq", 440, 20); external.Id = "example-external";
        engine.Saved.Graph.Nodes.Add(external); engine.Saved.Revision++;
        int history = mixer.EditHistory.Entries.Count;
        var save = mixer.PrepareForQuitAsync(CancellationToken.None);
        await engine.ConfigureEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        require(mixer.IsPreparingQuit && !mixer.CanEdit && !mixer.CanRestartEngine && mixer.PendingEditCount == 1,
            "Quit preparation suspends graph editing and restart while the final queued save remains in progress.");
        mixer.Edit(c => c.Graph.Nodes[0].GainDb = 4);
        await mixer.RetryAsync();
        var restart = await mixer.RestartEngineAsync();
        Exception? updateError = null;
        try { await mixer.PrepareForUpdateAsync(CancellationToken.None); }
        catch (InvalidOperationException error) { updateError = error; }
        require(!restart.Success && updateError is not null && engine.Calls.All(c => c is "snapshot" or "configure")
            && mixer.GraphConfig.Nodes[0].GainDb == -9,
            "Quit preparation prevents update, retry, restart and graph edits from racing its reversible save phase.");
        engine.HoldConfigure.SetResult();
        var result = await save;
        require(result.Saved && mixer.PendingEditCount == 0 && mixer.IsPreparingQuit
            && engine.Saved.Graph.Nodes.Any(n => n.Id == external.Id) && engine.Saved.Graph.Nodes[0].GainDb == -9
            && engine.Saved.Graph.PanX == 100 && engine.Saved.Graph.Zoom == .8 && mixer.EditHistory.Entries.Count == history
            && engine.Calls.All(c => c != "shutdown"),
            "Final save merges pending work onto newer saved changes, preserves history and view, and never tears down the engine during preparation.");
        mixer.CancelQuitPreparation();
        require(mixer.CanEdit && mixer.CanRestartEngine && !mixer.IsPreparingQuit,
            "Cancelling the reversible preparation returns an editable live session without cancelling its lifetime token.");
    }

    private static async Task FailedSaveRetryCancelAndCopy(Action<bool, string> require)
    {
        var engine = new Engine { RejectConfigure = true };
        using var mixer = Connected(engine);
        var addition = GraphDefaults.NewNode("eq", 120, 20); addition.Id = "example-queued";
        mixer.Edit(c => c.Graph.Nodes.Add(addition.Copy()));
        mixer.Edit(c => c.Graph.Nodes[0].GainDb = -17);
        var external = GraphDefaults.NewNode("mix", 600, 20); external.Id = "example-newer-saved";
        engine.Saved.Graph.Nodes.Add(external); engine.Saved.Revision++;
        int history = mixer.EditHistory.Entries.Count;
        var failed = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(!failed.Saved && failed.PendingChanges == 2 && mixer.PendingEditCount == 2
            && mixer.EditHistory.Entries.Count == history && mixer.IsPreparingQuit && engine.Calls.All(c => c != "shutdown"),
            "A final settings-write failure retains all queued operations and undo history before any shutdown request.");
        var recovery = JsonSerializer.Deserialize<MixerConfiguration>(mixer.SerializeQuitRecovery(), Protocol.Json)!;
        require(recovery.Graph.Nodes.Count(n => n.Id == addition.Id) == 1
            && recovery.Graph.Nodes.Any(n => n.Id == external.Id) && recovery.Graph.Nodes[0].GainDb == -17,
            "Recovery JSON contains the complete reconciled workspace, newer saved nodes and the queued graph edits.");
        engine.Offline = true;
        failed = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(!failed.Saved && mixer.SerializeQuitRecovery().Contains(external.Id, StringComparison.Ordinal),
            "If a retry loses the engine, recovery retains the newest successfully reconciled copy rather than reverting to an older graph.");
        string directory = Path.Combine(Path.GetTempPath(), "VineMix-quit-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "Example-recovery.json");
            mixer.WriteQuitRecoveryCopy(path);
            require(File.ReadAllText(path) == mixer.SerializeQuitRecovery() && mixer.PendingEditCount == 2,
                "Recovery writes a complete reusable workspace to its chosen new file without falsely acknowledging queued changes.");
            string original = File.ReadAllText(path); bool refused = false;
            try { mixer.WriteQuitRecoveryCopy(path); } catch (IOException) { refused = true; }
            require(refused && File.ReadAllText(path) == original && Directory.GetFiles(directory, "*.tmp").Length == 0,
                "Recovery refuses an existing file and cleans staging data, preserving both the existing file and pending edits.");
            bool failedWrite = false;
            try { mixer.WriteQuitRecoveryCopy(Path.Combine(directory, "missing", "Example-recovery.json")); }
            catch (IOException) { failedWrite = true; }
            require(failedWrite && mixer.PendingEditCount == 2 && mixer.IsPreparingQuit,
                "An unwritable recovery destination keeps quit paused and every pending operation available for another choice.");
        }
        finally { Directory.Delete(directory, recursive: true); }
        mixer.CancelQuitPreparation();
        require(mixer.CanEdit && mixer.CanRestartEngine && mixer.PendingEditCount == 2
            && mixer.GraphConfig.Nodes.Any(n => n.Id == addition.Id) && mixer.GraphConfig.Nodes[0].GainDb == -17,
            "Cancel after save and recovery failures returns to the same desired graph with pending edits intact.");
        engine.Offline = false; engine.RejectConfigure = false;
        var retry = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(retry.Saved && mixer.PendingEditCount == 0 && engine.Saved.Graph.Nodes.Count(n => n.Id == addition.Id) == 1,
            "A later retry saves the retained graph once after the underlying settings failure is repaired.");
    }

    private static async Task FailedPreferencesSave(Action<bool, string> require)
    {
        var engine = new Engine();
        using var mixer = Connected(engine);
        bool failWrite = true;
        string savedPreferences = "";
        mixer.PreviewUiPreferencesWriter = json =>
        {
            if (failWrite) throw new IOException("Example preference folder is read-only");
            savedPreferences = json;
        };
        mixer.Edit(configuration => configuration.Graph.Frames.Add(new GraphFrame
        {
            Id = "example-unsaved-group", Name = "Unsaved voice group", NodeIds = [configuration.Graph.Nodes[0].Id]
        }));
        var failed = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(!failed.Saved && mixer.IsPreparingQuit && mixer.PendingEditCount == 0
            && failed.Error.Contains("node groups", StringComparison.Ordinal) && engine.Calls.Count == 0
            && mixer.GraphConfig.Frames.Single().Name == "Unsaved voice group",
            "A failed group-preference write pauses Quit even without an engine edit, retaining the group and audio session.");
        bool refusedRecovery = false;
        try { mixer.SerializeQuitRecovery(); } catch (IOException) { refusedRecovery = true; }
        require(refusedRecovery && mixer.IsPreparingQuit && engine.Calls.Count == 0,
            "An engine-only recovery copy cannot authorize quitting while unsaved group preferences would be omitted.");
        mixer.CancelQuitPreparation();
        require(mixer.CanEdit && mixer.SaveStatus == "Workspace preferences need attention",
            "Cancelling after a preference failure restores editing without falsely reporting the group as saved.");
        Exception? updateError = null;
        try { await mixer.PrepareForUpdateAsync(CancellationToken.None); }
        catch (IOException error) { updateError = error; }
        require(updateError is not null && !mixer.IsPreparingUpdate && mixer.CanEdit && engine.Calls.Count == 0,
            "Update preparation also refuses an unsaved group before any configure or shutdown operation, retaining an editable session.");
        failWrite = false;
        var retry = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(retry.Saved && savedPreferences.Contains("Unsaved voice group", StringComparison.Ordinal)
            && engine.Calls.Count == 0 && mixer.PendingEditCount == 0,
            "A later successful preference retry preserves the group and permits ordinary Quit without an unnecessary engine edit.");
        mixer.CancelQuitPreparation();
        failWrite = true;
        mixer.ShowActionNotifications = false;
        failed = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(!failed.Saved && !mixer.ShowActionNotifications && engine.Calls.Count == 0,
            "The same save barrier retains ordinary unsaved desktop preferences, including the action-notification toggle.");
        failWrite = false;
        require((await mixer.PrepareForQuitAsync(CancellationToken.None)).Saved
            && savedPreferences.Contains("\"showActionNotifications\":false", StringComparison.Ordinal),
            "Retry persists the latest desktop preference values rather than an earlier group-only snapshot.");

        var changedEngine = new Engine();
        using var changed = Connected(changedEngine);
        bool failReconciliation = false;
        changed.PreviewUiPreferencesWriter = _ =>
        {
            if (failReconciliation) throw new IOException("Example preference failure after engine reconciliation");
        };
        changed.Edit(configuration => configuration.Graph.Frames.Add(new GraphFrame
        { Id = "example-pruned-group", Name = "Old group", NodeIds = [configuration.Graph.Nodes[0].Id] }));
        changedEngine.Saved.Graph.Nodes.Clear(); changedEngine.Saved.Revision++;
        failReconciliation = true;
        updateError = null;
        try { await changed.PrepareForUpdateAsync(CancellationToken.None); }
        catch (IOException error) { updateError = error; }
        require(updateError is not null && !changed.IsPreparingUpdate && changed.CanEdit
            && changedEngine.ConfigureCalls == 1 && changedEngine.Calls.All(command => command != "shutdown"),
            "An acknowledgement that removes obsolete group membership must also save those preferences before update preparation succeeds.");
        failReconciliation = false;
        await changed.PrepareForUpdateAsync(CancellationToken.None);
        changed.ResumeAfterUpdatePreparation();
        require(changed.GraphConfig.Frames.Count == 0 && changed.CanEdit,
            "Retry after a late preference failure persists the reconciled group removal and restores a usable session.");
    }

    private static async Task LostReplyAndCancelledReply(Action<bool, string> require)
    {
        var engine = new Engine { LoseReply = true };
        using (var mixer = Connected(engine))
        {
            var addition = GraphDefaults.NewNode("eq", 0, 0); addition.Id = "example-once";
            mixer.Edit(c => c.Graph.Nodes.Add(addition.Copy()));
            var result = await mixer.PrepareForQuitAsync(CancellationToken.None);
            require(result.Saved && engine.ConfigureCalls == 1 && mixer.PendingEditCount == 0
                && engine.Saved.Graph.Nodes.Count(n => n.Id == addition.Id) == 1,
                "A final save whose reply is lost is recognized from its exact saved graph before replaying an append.");
        }
        using var cancellation = new CancellationTokenSource();
        engine = new Engine { CancelAfterCommit = cancellation };
        using (var mixer = Connected(engine))
        {
            var addition = GraphDefaults.NewNode("eq", 0, 0); addition.Id = "example-after-timeout";
            mixer.Edit(c => c.Graph.Nodes.Add(addition.Copy()));
            var failed = await mixer.PrepareForQuitAsync(cancellation.Token);
            require(!failed.Saved && mixer.PendingEditCount == 1,
                "A cancelled final save retains the uncertain pending batch for recovery rather than claiming it was saved.");
            mixer.CancelQuitPreparation();
            engine.CancelAfterCommit = null;
            mixer.ApplyPreviewSnapshot(engine.Snapshot());
            require(mixer.PendingEditCount == 0 && engine.ConfigureCalls == 1
                && mixer.GraphConfig.Nodes.Count(n => n.Id == addition.Id) == 1,
                "After cancelling Quit, normal snapshot reconciliation recognizes a committed lost reply and does not duplicate the queued append.");
        }
    }

    private static async Task ReplayConflict(Action<bool, string> require)
    {
        var engine = new Engine(); using var mixer = Connected(engine);
        bool permitted = true;
        mixer.TryEdit((MixerConfiguration configuration, out string error) =>
        {
            error = permitted ? "" : "Example target changed";
            if (!permitted) return false;
            configuration.Graph.Nodes[0].GainDb = -22; return true;
        }, out _);
        permitted = false;
        var result = await mixer.PrepareForQuitAsync(CancellationToken.None);
        var copy = JsonSerializer.Deserialize<MixerConfiguration>(mixer.SerializeQuitRecovery(), Protocol.Json)!;
        require(!result.Saved && mixer.PendingEditCount == 1 && copy.Graph.Nodes[0].GainDb == -22
            && engine.ConfigureCalls == 0 && mixer.EditHistory.Entries.Count > 0,
            "A final-save replay conflict preserves the original desired graph, queue and history for recovery instead of rejecting and losing the edit.");
    }

    private static async Task FlowChoices(Action<bool, string> require)
    {
        int saves = 0, stops = 0, choices = 0, cancels = 0;
        var failure = new QuitSaveResult(false, "Example disk failure", 2);
        bool completed = await QuitFlow.RunAsync(() => Task.FromResult(new QuitSaveResult(true, "", 0)),
            _ => { choices++; return Task.FromResult(QuitChoice.Cancel); },
            () => throw new InvalidOperationException("Unexpected recovery"), () => cancels++, () => { stops++; return Task.CompletedTask; });
        require(completed && stops == 1 && choices == 0 && cancels == 0,
            "A successful ordinary Quit stops once with no recovery prompt.");

        stops = choices = 0;
        completed = await QuitFlow.RunAsync(() => Task.FromResult(++saves == 1 ? failure : new(true, "", 0)),
            _ => { choices++; return Task.FromResult(QuitChoice.Retry); },
            () => throw new InvalidOperationException("Unexpected recovery"), () => cancels++, () => { stops++; return Task.CompletedTask; });
        require(completed && saves == 2 && choices == 1 && stops == 1 && cancels == 0,
            "Retry repeats saving and performs teardown only after the retry succeeds.");

        stops = 0; var decisions = new Queue<QuitChoice>([QuitChoice.RecoveryCopy, QuitChoice.RecoveryCopy, QuitChoice.Cancel]);
        int copies = 0; string detail = "";
        completed = await QuitFlow.RunAsync(() => Task.FromResult(failure),
            state => { detail = state.Error; return Task.FromResult(decisions.Dequeue()); },
            () => Task.FromResult(++copies == 1 ? new QuitRecoveryResult(false, true, "") : new(false, false, "Example destination full")),
            () => cancels++, () => { stops++; return Task.CompletedTask; });
        require(!completed && copies == 2 && stops == 0 && cancels == 1 && detail.Contains("Example destination full", StringComparison.Ordinal),
            "Cancelling a recovery picker or failing its write returns to the decision; Cancel keeps the engine alive and reports failed-copy details.");

        foreach (var choice in new[] { QuitChoice.RecoveryCopy, QuitChoice.Discard })
        {
            stops = copies = 0;
            completed = await QuitFlow.RunAsync(() => Task.FromResult(failure), _ => Task.FromResult(choice),
                () => { copies++; return Task.FromResult(new QuitRecoveryResult(true, false, "")); },
                () => throw new InvalidOperationException("Unexpected cancel"), () => { stops++; return Task.CompletedTask; });
            require(completed && stops == 1 && copies == (choice == QuitChoice.RecoveryCopy ? 1 : 0),
                choice == QuitChoice.RecoveryCopy ? "A successful recovery copy permits shutdown exactly once."
                    : "Only the explicit Quit without saving choice permits shutdown without a save or recovery copy.");
        }
    }

    private static async Task FailedUpdateHealthQuit(Action<bool, string> require)
    {
        var engine = new Engine(); using var mixer = Connected(engine);
        mixer.SetUpdateHealthMode(true);
        bool refused = false;
        try { await mixer.PrepareForQuitAsync(CancellationToken.None); }
        catch (InvalidOperationException) { refused = true; }
        require(refused && !mixer.CanQuitAfterFailedUpdateHealth && !mixer.IsPreparingQuit && engine.Calls.Count == 0,
            "An active update health check refuses ordinary Quit without changing its read-only barrier or touching the engine.");
        mixer.AllowQuitAfterFailedUpdateHealth();
        var result = await mixer.PrepareForQuitAsync(CancellationToken.None);
        require(result.Saved && mixer.CanQuitAfterFailedUpdateHealth && mixer.IsUpdateHealthMode && !mixer.CanEdit
            && !mixer.CanRestartEngine && engine.Calls.Count == 0,
            "After health recovery reports failure, explicit Quit can close the read-only session without reopening editing or writing its protected settings.");

        var withPending = new Engine(); using var pending = Connected(withPending);
        pending.Edit(configuration => configuration.Graph.Nodes[0].GainDb = -14);
        pending.SetUpdateHealthMode(true); pending.AllowQuitAfterFailedUpdateHealth();
        var failure = await pending.PrepareForQuitAsync(CancellationToken.None);
        require(!failure.Saved && failure.PendingChanges == 1 && pending.PendingEditCount == 1
            && withPending.Calls.Count == 0 && pending.SerializeQuitRecovery().Contains("-14", StringComparison.Ordinal),
            "Even an unexpected pending edit in failed update health requires recovery or an explicit discard choice; it is never silently lost or saved into the protected workspace.");
        pending.CancelQuitPreparation();
        require(pending.IsUpdateHealthMode && !pending.CanEdit && !pending.CanRestartEngine && pending.PendingEditCount == 1,
            "Cancelling failed-health Quit preserves both the unsaved edit and the update health safety barrier.");
    }

    private static async Task SettingsAndDialog(Action<bool, string> require)
    {
        using var mixer = new MixerViewModel(preview: true);
        int quits = 0;
        var menu = MainWindow.CreateSettingsMenu(mixer, quit: () => { quits++; return Task.CompletedTask; });
        var last = menu.Items.OfType<MenuItem>().Last(item => item.Header is string);
        require((string)last.Header == "Quit VineMix" && AutomationProperties.GetName(last) == "Quit VineMix"
            && AutomationProperties.GetHelpText(last) == "Save your workspace and stop audio."
            && last.ToolTip is StackPanel tip && tip.Children.OfType<TextBlock>().Select(block => block.Text)
                .SequenceEqual(["Quit VineMix", "Save your workspace and stop audio."]),
            "Settings ends with an accessible Quit VineMix action and concise save-and-stop hover help.");
        last.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        require(quits == 1 && !menu.IsOpen && PresentationSource.FromVisual(menu) is null,
            "The detached Settings quit handler invokes the shared quit callback once without a native popup or engine operation.");
        var choices = new List<QuitChoice>();
        var content = (StackPanel)MainWindow.CreateQuitRecoveryContent(new(false, "Example folder is read-only", 2), choices.Add);
        var buttons = content.Children.OfType<Button>().ToArray();
        foreach (var button in buttons) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        require(choices.SequenceEqual([QuitChoice.Retry, QuitChoice.RecoveryCopy, QuitChoice.Cancel, QuitChoice.Discard])
            && buttons.Single(button => button.IsCancel).IsDefault
            && buttons.All(button => AutomationProperties.GetName(button).Length > 0 && AutomationProperties.GetHelpText(button).Length > 0),
            "Recovery exposes all four distinct accessible choices, with Cancel as the default and Escape action.");
        content.Width = 440;
        foreach (double dpi in new[] { 96d, 144d, 192d })
        {
            VisualTreeHelper.SetRootDpi(content, new DpiScale(dpi / 96, dpi / 96));
            content.Measure(new Size(440, double.PositiveInfinity));
            content.Arrange(new Rect(content.DesiredSize)); content.UpdateLayout();
            require(buttons.All(button => button.ActualWidth > 0 && button.ActualHeight >= 34
                && button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize)).Right <= content.ActualWidth)
                && content.ActualHeight < 580,
                $"All four quit recovery choices fit the compact dialog at DPI {dpi} with readable labels and independent failure-detail scrolling.");
        }
        await Task.CompletedTask;
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static MixerViewModel Connected(Engine engine)
    {
        var mixer = new MixerViewModel(engine.Operations()); mixer.ApplyPreviewSnapshot(engine.Snapshot()); return mixer;
    }
    private sealed class Engine
    {
        internal MixerConfiguration Saved = new() { Version = 2, Revision = 3,
            Graph = new() { Nodes = [new GraphNode { Id = "example-quit-mix", Kind = "mix", Name = "Example quit mix" }] } };
        internal bool RejectConfigure, Offline, LoseReply;
        internal int ConfigureCalls;
        internal TaskCompletionSource? HoldConfigure;
        internal CancellationTokenSource? CancelAfterCommit;
        internal readonly TaskCompletionSource ConfigureEntered = Signal();
        internal readonly List<string> Calls = [];
        internal EngineRestartOperations Operations() => new(Request, _ => throw new InvalidOperationException("Unexpected restart"), () => { });
        internal MixerSnapshot Snapshot() => new() { SettingsState = "loaded", Configuration = Saved.Copy(), EngineProtocolVersion = 1,
            EngineVersion = ReleaseInfo.Version, EnginePid = 4242, SettingsPath = "example-no-file", PhysicalSettingsPath = "example-no-physical-file" };
        private async Task<ProtocolResponse> Request(string command, MixerConfiguration? configuration, ulong? revision, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls.Add(command);
            if (Offline) throw new IOException("Example engine unavailable");
            if (command == "configure")
            {
                ConfigureCalls++; ConfigureEntered.TrySetResult();
                if (HoldConfigure is not null) await HoldConfigure.Task.WaitAsync(token);
                if (RejectConfigure) return new() { Error = "Example settings folder is read-only", ErrorCode = "settings_write_failed" };
                if (configuration is null || revision != Saved.Revision) return new() { Error = "Example stale revision" };
                Saved = configuration.Copy(); Saved.Revision++;
                if (CancelAfterCommit is not null) { CancelAfterCommit.Cancel(); token.ThrowIfCancellationRequested(); }
                if (LoseReply) { LoseReply = false; throw new IOException("Example lost configure reply"); }
            }
            else if (command != "snapshot") throw new InvalidOperationException("Unexpected quit request " + command);
            return new() { Ok = true, Snapshot = Snapshot() };
        }
    }
}
