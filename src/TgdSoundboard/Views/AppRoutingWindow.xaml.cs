using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TgdSoundboard.Services;

namespace TgdSoundboard.Views;

public partial class AppRoutingWindow : Window
{
    private readonly Dictionary<int, bool> _capturedApps = new();

    public AppRoutingWindow()
    {
        InitializeComponent();
        LoadApps();
    }

    private void LoadApps()
    {
        var apps = AppAudioService.GetRunningAudioApps();

        // Restore capture state
        foreach (var app in apps)
        {
            if (_capturedApps.TryGetValue(app.ProcessId, out var captured))
            {
                app.RouteToVirtualCable = captured;
            }
        }

        AppList.ItemsSource = apps;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadApps();
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.Tag is int processId)
        {
            AppAudioService.SetAppVolume(processId, (float)e.NewValue);
        }
    }

    private void CaptureToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && toggle.Tag is int processId)
        {
            var capture = toggle.IsChecked ?? false;
            _capturedApps[processId] = capture;

            if (capture)
            {
                // Start capturing this app's audio
                StartAppCapture(processId);
            }
            else
            {
                // Stop capturing
                StopAppCapture(processId);
            }
        }
    }

    private void StartAppCapture(int processId)
    {
        // Note: True per-app audio capture requires either:
        // 1. Windows 10 2004+ AudioGraph API with AppCapture
        // 2. WASAPI process loopback (Windows 10 2004+)
        // 3. Third-party audio driver/hook
        //
        // For now, we'll use the Windows Sound Settings approach
        // which opens the Windows sound mixer for manual routing

        try
        {
            // Open Windows sound settings for manual per-app routing
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:apps-volume",
                UseShellExecute = true
            });

            MessageBox.Show(
                $"Windows Sound Settings opened.\n\n" +
                $"To route this app to the virtual cable:\n" +
                $"1. Find the app in the list\n" +
                $"2. Change its 'Output' to 'CABLE Input'\n\n" +
                $"This allows the app's audio to be captured by the soundboard.",
                "App Routing",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch
        {
            // Fallback for older Windows
            System.Diagnostics.Process.Start("sndvol.exe");
        }
    }

    private void StopAppCapture(int processId)
    {
        // Reset would require setting back to default device
        // For now just update the UI state
    }
}
