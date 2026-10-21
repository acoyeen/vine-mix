using System.Windows;

namespace VineMix.Desktop;

public partial class MainWindow
{
    private async void RecoveryRetry_Click(object sender, RoutedEventArgs e)
    { if (DataContext is MixerViewModel model) await model.RecoverSettingsAsync("recoveryRetry"); }
    private async void RecoveryRestore_Click(object sender, RoutedEventArgs e)
    { if (DataContext is MixerViewModel model) await model.RecoverSettingsAsync("restoreBackup"); }
    private async void RecoveryFresh_Click(object sender, RoutedEventArgs e)
    { if (DataContext is MixerViewModel model) await model.RecoverSettingsAsync("startFresh"); }
}
