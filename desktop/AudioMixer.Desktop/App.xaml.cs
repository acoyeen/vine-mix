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

namespace AudioMixer.Desktop;

public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private Forms.NotifyIcon? _tray;
    private MixerViewModel? _mixer;
    private MainWindow? _window;
    private bool _quitting;
    private System.Windows.Threading.DispatcherTimer? _previewTelemetry;
    public bool IsQuitting => _quitting;

    private void OnComboBoxPreviewMouseWheel(object sender, MouseWheelEventArgs args)
    {
        // Consume preview input before WPF promotes it to the selection-changing
        // MouseWheel event. Open dropdowns retain their normal list scrolling.
        if (sender is ComboBox { IsDropDownOpen: false }) args.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--render-design", StringComparer.OrdinalIgnoreCase))
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
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await renderer.RunAsync(); Shutdown(0); }
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
                Dispatcher.BeginInvoke(new Action(async () =>
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
        string identity = EngineConnection.UserSid;
        _instance = new Mutex(true, "Local\\AudioMixer.Desktop." + identity, out bool first);
        if (!first)
        {
            try { using var signal = EventWaitHandle.OpenExisting("Local\\AudioMixer.Show." + identity); signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AudioMixer.Show." + identity);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer)), null, Timeout.Infinite, false);
        _mixer = new MixerViewModel();
        _window = new MainWindow(_mixer);
        MainWindow = _window;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Vine Mix", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer)));
        menu.Items.Add("Windows sound settings", null, (_, _) => OpenExternal("ms-settings:sound"));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit Vine Mix", null, (_, _) => Dispatcher.BeginInvoke(new Action(async () => await QuitAsync())));
        _tray = new Forms.NotifyIcon { Text = "Vine Mix", Icon = System.Drawing.SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowMixer));
        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase)) _window.Show();
        else _mixer.IsVisible = false;
        _mixer.Start();
    }

    public void ShowMixer()
    {
        if (_window is null || _quitting) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Focus();
    }
    public async Task QuitAsync()
    {
        if (_quitting) return;
        _quitting = true;
        if (_tray is not null) _tray.Visible = false;
        if (_window is not null) _window.IsEnabled = false;
        try
        {
            if (_mixer is not null)
            {
                string warning = await _mixer.StopAsync();
                if (warning.Length > 0) MessageBox.Show(warning, "Vine Mix · Settings not saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        finally { Shutdown(); }
    }

    public static void OpenExternal(string destination)
    {
        try { Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        { MessageBox.Show(error.Message, "Could not open destination", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _previewTelemetry?.Stop();
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _tray?.Dispose();
        _mixer?.Dispose();
        if (_instance is not null)
        {
            try { _instance.ReleaseMutex(); } catch (ApplicationException) { }
            _instance.Dispose();
        }
        base.OnExit(e);
    }
}
