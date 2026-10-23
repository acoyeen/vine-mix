using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Forms = System.Windows.Forms;

namespace VineMix.Desktop;

public partial class App : Application
{
    private SessionOwnership? _instance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private EventWaitHandle? _legacyShowEvent;
    private RegisteredWaitHandle? _legacyShowWait;
    private Forms.NotifyIcon? _tray;
    private Forms.ContextMenuStrip? _trayMenu;
    private TrayBranding? _trayBranding;
    private MixerViewModel? _mixer;
    private MainWindow? _window;
    private bool _quitting;
    private bool _quitRequested;
#if VINEMIX_VERIFICATION
    private System.Windows.Threading.DispatcherTimer? _previewTelemetry;
#endif
    public bool IsQuitting => _quitting;

    private void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        // Consume preview input before WPF promotes it to the selection-changing
        // MouseWheel event. Open dropdowns retain their normal list scrolling.
        if (sender is ComboBox { IsDropDownOpen: false }) args.Handled = true;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        HoverHelp.Register();
        base.OnStartup(e);
        int smoke = Array.FindIndex(e.Args, arg => arg.Equals("--release-smoke", StringComparison.OrdinalIgnoreCase));
        if (smoke >= 0)
        {
            try { Shutdown(smoke + 1 < e.Args.Length ? BetaReleaseSmoke.Run(e.Args[smoke + 1]) : 1); }
            catch (Exception error) { Debug.WriteLine(error); Shutdown(1); }
            return;
        }
#if VINEMIX_VERIFICATION
        if (e.Args.Contains("--render-design", StringComparer.OrdinalIgnoreCase)
            || e.Args.Contains("--render-beta-docs", StringComparer.OrdinalIgnoreCase))
        {
            // This branch precedes preview windows, the singleton/show event,
            // tray setup and every engine connection. The renderer uses detached
            // content only and must never call Window.Show or create an HWND.
            OffscreenDesignRenderer renderer;
            try { renderer = new OffscreenDesignRenderer(e.Args); }
            catch (Exception error) { Debug.WriteLine(error); Shutdown(1); return; }
            DispatcherUnhandledException += (_, error) =>
            {
                try { renderer.WriteFailure(error.Exception); } catch (Exception reportError) { Debug.WriteLine(reportError); }
                error.Handled = true; Shutdown(1);
            };
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (e.Args.Contains("--render-beta-docs", StringComparer.OrdinalIgnoreCase)) await renderer.RunBetaDocsAsync();
                    else await renderer.RunAsync();
                    Shutdown(0);
                }
                catch (Exception error)
                {
                    try { renderer.WriteFailure(error); } catch (Exception reportError) { Debug.WriteLine(reportError); }
                    Shutdown(1);
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        if (e.Args.Contains("--preview", StringComparer.OrdinalIgnoreCase) || e.Args.Contains("--verify-ui", StringComparer.OrdinalIgnoreCase))
        {
            if (e.Args.Contains("--verify-ui", StringComparer.OrdinalIgnoreCase))
                DispatcherUnhandledException += (_, error) => { GraphUiVerification.WriteFailure(e.Args, error.Exception); error.Handled = true; Shutdown(1); };
            _mixer = new MixerViewModel(preview: true);
            _mixer.ApplyPreviewSnapshot(UiPreview.CreateSnapshot(e.Args));
            _window = new MainWindow(_mixer); MainWindow = _window;
            if (e.Args.Contains("--verify-ui", StringComparer.OrdinalIgnoreCase))
            {
                _window.Show();
                _ = Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { await GraphUiVerification.RunAsync(_window, _mixer, e.Args); Shutdown(0); }
                    catch (Exception error) { GraphUiVerification.WriteFailure(e.Args, error); Shutdown(1); }
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            else
            {
                _window.Show();
                _previewTelemetry = new System.Windows.Threading.DispatcherTimer(TimeSpan.FromSeconds(1), System.Windows.Threading.DispatcherPriority.Background, (_, _) => _mixer.RefreshPreviewTelemetry(), Dispatcher);
            }
            return;
        }
#endif
        if (e.Args.Any(arg => arg.Equals("--render-design", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--render-beta-docs", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--preview", StringComparison.OrdinalIgnoreCase) || arg.Equals("--verify-ui", StringComparison.OrdinalIgnoreCase)))
        { Shutdown(2); return; }
        var standalone = StandaloneLaunch.EnsureStandalone(
            Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "VineMix.exe"), e.Args, Environment.CurrentDirectory);
        if (standalone.State != StandaloneLaunchState.Continue)
        {
            if (standalone.State == StandaloneLaunchState.Failed)
                MessageBox.Show("VineMix could not open its normal saved workspace. Please try opening it again.\n\n" + standalone.Error,
                    "VineMix · Could not start", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(standalone.State == StandaloneLaunchState.Failed ? 1 : 0);
            return;
        }
        int worker = Array.FindIndex(e.Args, arg => arg.Equals("--apply-update", StringComparison.OrdinalIgnoreCase));
        if (worker >= 0)
        {
            if (worker + 1 >= e.Args.Length) { Shutdown(2); return; }
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { Shutdown(await UpdateWorker.RunAsync(e.Args[worker + 1])); }
                catch (Exception error) { Debug.WriteLine(error); Shutdown(1); }
            }));
            return;
        }
        int health = Array.FindIndex(e.Args, arg => arg.Equals("--update-health", StringComparison.OrdinalIgnoreCase));
        if (health >= 0)
        {
            if (health + 1 >= e.Args.Length) { Shutdown(2); return; }
            try
            {
                _updateHealthTransaction = Path.GetFullPath(e.Args[health + 1]);
                var expected = await UpdateTransaction.WaitForCandidateRegistrationAsync(_updateHealthTransaction);
                int selected = Array.FindIndex(e.Args, arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase));
                if (expected.Version != ReleaseInfo.Version || expected.SettingsPath is null
                    || selected < 0 || selected + 1 >= e.Args.Length
                    || !UpdateFiles.SamePath(Path.GetFullPath(e.Args[selected + 1]), expected.SettingsPath))
                    throw new IOException("This update candidate does not match its approved workspace and version.");
            }
            catch (Exception error) { Debug.WriteLine(error); Shutdown(1); return; }
        }
        try { if (HandlePendingUpdates()) { Shutdown(); return; } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            MessageBox.Show(error.Message, "VineMix update recovery", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1); return;
        }
        string identity = EngineConnection.UserSid;
        _instance = new SessionOwnership();
        if (!_instance.Acquire("Desktop"))
        {
            foreach (string product in new[] { "AudioMixer", "VineMix" })
            {
                try { using var signal = EventWaitHandle.OpenExisting($"Local\\{product}.Show." + identity); signal.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
            }
            Shutdown(); return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\VineMix.Show." + identity);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer)), null, Timeout.Infinite, false);
        _legacyShowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AudioMixer.Show." + identity);
        _legacyShowWait = ThreadPool.RegisterWaitForSingleObject(_legacyShowEvent, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer)), null, Timeout.Infinite, false);
        try
        {
            int settings = Array.FindIndex(e.Args, arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase));
            if (settings >= 0)
            {
                if (settings + 1 >= e.Args.Length) throw new IOException("--settings requires a workspace filename.");
                WorkspacePaths.CustomSettings = Path.GetFullPath(e.Args[settings + 1]);
            }
            string engine = EmbeddedRuntime.ResolveEnginePath();
            bool engineRunning = SessionOwnership.ValidateEngine(engine);
            EngineSupervisor.RuntimeEnginePath = engine;
            if (WorkspacePaths.CustomSettings is null)
            {
                using var migrationGuard = new SessionOwnership();
                if (migrationGuard.Acquire("Engine"))
                    WorkspaceMigration.Run(WorkspacePaths.DataDirectory, WorkspaceMigration.LegacyDirectories(), WorkspaceMigration.ChooseSource,
                        validate: stage => WorkspaceMigration.ValidateStaging(stage, engine));
                else if (!engineRunning || Directory.Exists(Path.Combine(WorkspacePaths.DataDirectory, ".migration-staging"))
                        && !File.Exists(Path.Combine(WorkspacePaths.DataDirectory, "migration.complete.json")))
                    throw new IOException("Quit the existing VineMix session before migrating this workspace.");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(error.Message, "VineMix · Could not open workspace", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(1); return;
        }
        AppLifetimeLog.Start();
        DispatcherUnhandledException += (_, error) => AppLifetimeLog.Write("Unhandled dispatcher exception: " + error.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, error) => AppLifetimeLog.Write("Unhandled process exception: " + error.ExceptionObject);
        _mixer = new MixerViewModel();
        if (_updateHealthTransaction is not null) _mixer.SetUpdateHealthMode(true);
        _window = new MainWindow(_mixer);
        if (_updateHealthTransaction is not null) _window.IsEnabled = false;
        MainWindow = _window;
        _trayBranding = new TrayBranding();
        var menu = _trayMenu = new Forms.ContextMenuStrip { Font = _trayBranding.MenuFont };
        menu.Items.Add("Open VineMix", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer)));
        menu.Items.Add("Windows sound settings", null, (_, _) => OpenExternal("ms-settings:sound"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit VineMix", null, (_, _) => Dispatcher.BeginInvoke(new Action(async () => await QuitAsync())));
        _tray = new Forms.NotifyIcon { Text = "VineMix", Icon = _trayBranding.Icon, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer));
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) _window.Show();
        else _mixer.IsVisible = false;
        _window.BeginWhatsNew(System.IO.File.Exists(WorkspacePaths.Settings));
        _mixer.Start();
        if (_updateHealthTransaction is not null) _ = ObserveUpdatedCandidateAsync(_updateHealthTransaction);
        else if (ReadUpdateRollbackNotice(e.Args) is { } rollbackNotice) _window.Workspace.SetMessage(rollbackNotice, true);
        else _ = _window.BeginAutomaticUpdateCheckAsync();
    }

    public void ShowMixer()
    {
        if (_window is null || _quitting || _quitRequested) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Focus();
    }
    public async Task QuitAsync()
    {
        if (_quitting || _quitRequested || _updating
            || _mixer is { IsUpdateHealthMode: true, CanQuitAfterFailedUpdateHealth: false }) return;
        if (_window is not null && !_window.TryPrepareQuitEditors()) { ShowMixer(); return; }
        if (_mixer?.IsRestartingEngine == true)
        { _window?.Workspace.SetMessage("Wait for the audio engine restart to finish before quitting.", true); ShowMixer(); return; }
        _quitRequested = true;
        if (_window is not null) _window.IsEnabled = false;
        try
        {
            if (_mixer is null || _window is null) { _quitting = true; Shutdown(); return; }
            await QuitFlow.RunAsync(async () =>
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    return await _mixer.PrepareForQuitAsync(deadline.Token);
                }, _window.ChooseQuitRecoveryAsync, _window.SaveQuitRecoveryCopyAsync,
                _mixer.CancelQuitPreparation, async () =>
                {
                    _quitting = true;
                    if (_tray is not null) _tray.Visible = false;
                    await _mixer.StopAsync();
                    Shutdown();
                });
        }
        catch (Exception error)
        {
            if (_quitting) { Debug.WriteLine(error); Shutdown(); }
            else
            {
                _mixer?.CancelQuitPreparation();
                _window?.Workspace.SetMessage("VineMix stayed open because quitting did not finish. " + error.Message, true);
            }
        }
        finally
        {
            if (!_quitting)
            {
                _quitRequested = false;
                if (_window is not null) _window.IsEnabled = true;
                ShowMixer();
            }
        }
    }

    public static void OpenExternal(string destination)
    {
        try { Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        { MessageBox.Show(error.Message, "Could not open destination", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLifetimeLog.Write($"Exiting code={e.ApplicationExitCode} explicitQuit={_quitting}");
#if VINEMIX_VERIFICATION
        _previewTelemetry?.Stop();
#endif
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _legacyShowWait?.Unregister(null);
        _legacyShowEvent?.Dispose();
        _tray?.Dispose();
        _trayMenu?.Dispose();
        _trayBranding?.Dispose();
        _window?.StopUpdateChecks();
        _mixer?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
