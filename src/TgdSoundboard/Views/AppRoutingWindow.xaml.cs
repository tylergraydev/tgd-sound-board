using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TgdSoundboard.Services;

namespace TgdSoundboard.Views;

public partial class AppRoutingWindow : Window
{
    private List<AudioApp>? _apps;

    public AppRoutingWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set initial state of master toggle
        MasterRoutingToggle.IsChecked = App.AudioPlayback.IsAppRoutingEnabled;
        LoadApps();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Keep routing active even when window closes
    }

    private void LoadApps()
    {
        _apps = AppAudioService.GetRunningAudioApps();

        // Filter out the soundboard itself
        var currentProcessId = Environment.ProcessId;
        _apps = _apps.Where(a => a.ProcessId != currentProcessId).ToList();

        // Mark apps as routed based on current routing state
        foreach (var app in _apps)
        {
            app.IsRouted = App.AudioPlayback.IsAppRouted(app.ProcessId);
        }

        AppList.ItemsSource = _apps;
        EmptyState.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadApps();
    }

    private void MasterRoutingToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MasterRoutingToggle.IsChecked == true)
        {
            try
            {
                App.AudioPlayback.EnableAppRouting();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not enable audio routing: {ex.Message}",
                    "Routing Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                MasterRoutingToggle.IsChecked = false;
            }
        }
        else
        {
            App.AudioPlayback.DisableAppRouting();
        }
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.Tag is int processId)
        {
            AppAudioService.SetAppVolume(processId, (float)e.NewValue);
        }
    }

    private void MuteToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && toggle.Tag is int processId)
        {
            var muted = toggle.IsChecked ?? false;
            AppAudioService.SetAppMute(processId, muted);
        }
    }
}
