using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using VineMix.Desktop.Controls;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private readonly Path _layoutLockBody = LockStroke("M 5,10 L 17,10 Q 18,10 18,11 L 18,20 Q 18,21 17,21 L 5,21 Q 4,21 4,20 L 4,11 Q 4,10 5,10 Z M 11,14 L 11,17");
    private readonly Path _layoutLockShackle = LockStroke("M 6,10 L 6,7 A 5,5 0 0 1 16,7 L 16,10");
    private readonly RotateTransform _layoutLockHinge = new(50, 16, 10);
    private readonly ScaleTransform _layoutLockEmphasis = new(1, 1);
    private readonly SolidColorBrush _layoutLockInk = new();
    private readonly DispatcherTimer _layoutLockFeedbackTimer = new() { Interval = TimeSpan.FromMilliseconds(710) };
    private bool _layoutLockSubscribed;
    // Detached verification only; never changes the Windows preference.
    internal bool? PreviewLayoutLockMotion { get; set; }
    private bool LayoutLockMotionEnabled => PreviewLayoutLockMotion ?? SystemParameters.ClientAreaAnimation;
    private bool LayoutLockHighContrast => ConsoleTheme.PreviewHighContrast ?? SystemParameters.HighContrast;
    private Color LayoutLockRestingColor => LayoutLockHighContrast || !_mixer.IsLayoutLocked
        ? ((SolidColorBrush)ConsoleTheme.Get(LayoutLockHighContrast ? "ConsoleTextBrush" : "ConsoleMutedBrush")).Color
        : Color.FromRgb(255, 100, 100);
    private static Path LockStroke(string data) => new()
    {
        Data = Geometry.Parse(data), StrokeThickness = 2, Stretch = Stretch.None,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false
    };
    private void InitializeLayoutLock()
    {
        _layoutLockShackle.RenderTransform = _layoutLockHinge;
        var drawing = new Canvas { Width = 24, Height = 24, IsHitTestVisible = false };
        drawing.Children.Add(_layoutLockShackle); drawing.Children.Add(_layoutLockBody);
        var icon = new Viewbox
        {
            Width = 16, Height = 16, Child = drawing, IsHitTestVisible = false,
            RenderTransform = _layoutLockEmphasis, RenderTransformOrigin = new Point(.5, .5)
        };
        var content = new Grid { Width = 22, Height = 20 };
        content.Children.Add(icon);
        LayoutLockButton.Content = content;
        _layoutLockFeedbackTimer.Tick += (_, _) => ClearLayoutLockFeedback();
        UpdateLayoutLock();
        Loaded += (_, _) =>
        {
            if (_layoutLockSubscribed) return;
            _layoutLockSubscribed = true;
            _mixer.PropertyChanged += LayoutLockPropertyChanged;
            _mixer.LayoutEditBlocked += LayoutEditBlocked;
            ConsoleTheme.Changed += LayoutLockThemeChanged;
            UpdateLayoutLock();
        };
        Unloaded += (_, _) =>
        {
            _layoutLockSubscribed = false;
            _mixer.PropertyChanged -= LayoutLockPropertyChanged;
            _mixer.LayoutEditBlocked -= LayoutEditBlocked;
            ConsoleTheme.Changed -= LayoutLockThemeChanged;
            ClearLayoutLockFeedback();
            _layoutLockHinge.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }
    private void LayoutLock_Click(object sender, RoutedEventArgs args)
    {
        _mixer.IsLayoutLocked = !_mixer.IsLayoutLocked;
        // Also works for detached previews before the window's Loaded event.
        if (!_layoutLockSubscribed) UpdateLayoutLock(animate: true);
    }
    private void LayoutLockPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MixerViewModel.IsLayoutLocked)) UpdateLayoutLock(animate: true);
    }
    private void LayoutLockThemeChanged(object? sender, EventArgs args) => UpdateLayoutLock();
    private void LayoutEditBlocked(object? sender, EventArgs args) => ShowLayoutLockFeedback();
    internal void UpdateLayoutLock(bool animate = false)
    {
        bool locked = _mixer.IsLayoutLocked;
        ClearLayoutLockFeedback();
        double previousAngle = _layoutLockHinge.Angle, targetAngle = locked ? 0 : 50;
        _layoutLockHinge.BeginAnimation(RotateTransform.AngleProperty, null);
        _layoutLockHinge.Angle = targetAngle;
        if (animate && LayoutLockMotionEnabled && Math.Abs(previousAngle - targetAngle) > .01)
            _layoutLockHinge.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(previousAngle, targetAngle,
                TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop });
        _layoutLockBody.Stroke = _layoutLockShackle.Stroke = _layoutLockInk;
        string state = locked ? "Layout locked" : "Layout unlocked";
        string action = locked ? "Unlock layout" : "Lock layout";
        const string help = "Locks node positions and cables. Sound controls stay available.";
        LayoutLockButton.ToolTip = NodeToolTip.Create(action, locked ? "Allow node and cable changes." : "Keep nodes and cables in place.");
        AutomationProperties.SetName(LayoutLockButton, state + ". " + action);
        AutomationProperties.SetItemStatus(LayoutLockButton, locked ? "Locked" : "Unlocked");
        AutomationProperties.SetHelpText(LayoutLockButton, help);
        AnnounceLayoutLock();
    }
    internal void ShowLayoutLockFeedback()
    {
        if (!_mixer.IsLayoutLocked || _layoutLockFeedbackTimer.IsEnabled) return;
        // Grow quickly, then settle back more slowly; the colour stays constant.
        AutomationProperties.SetItemStatus(LayoutLockButton, "Layout locked. Unlock to move nodes or change connections.");
        AnnounceLayoutLock();
        if (LayoutLockMotionEnabled)
        {
            var emphasis = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            emphasis.KeyFrames.Add(new EasingDoubleKeyFrame(1.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            emphasis.KeyFrames.Add(new DiscreteDoubleKeyFrame(1.4, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
            emphasis.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(660)))
                { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
            _layoutLockEmphasis.BeginAnimation(ScaleTransform.ScaleXProperty, emphasis);
            _layoutLockEmphasis.BeginAnimation(ScaleTransform.ScaleYProperty, emphasis);
        }
        else { _layoutLockEmphasis.ScaleX = _layoutLockEmphasis.ScaleY = 1.4; }
        _layoutLockFeedbackTimer.Start();
    }
    private void ClearLayoutLockFeedback()
    {
        _layoutLockFeedbackTimer.Stop();
        _layoutLockInk.Color = LayoutLockRestingColor;
        _layoutLockEmphasis.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _layoutLockEmphasis.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        _layoutLockEmphasis.ScaleX = _layoutLockEmphasis.ScaleY = 1;
        AutomationProperties.SetItemStatus(LayoutLockButton, _mixer.IsLayoutLocked ? "Locked" : "Unlocked");
    }
    private void AnnounceLayoutLock()
    {
        if (!IsLoaded) return;
        var peer = FrameworkElementAutomationPeer.FromElement(LayoutLockButton) ?? FrameworkElementAutomationPeer.CreatePeerForElement(LayoutLockButton);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
