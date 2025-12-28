using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TgdSoundboard.Services;

namespace TgdSoundboard.Views;

public partial class MixerWindow : Window
{
    private readonly AudioMixerService _mixerService;

    public MixerWindow()
    {
        InitializeComponent();
        _mixerService = new AudioMixerService();
        _mixerService.LevelUpdated += OnLevelUpdated;
        _mixerService.SourcesUpdated += OnSourcesUpdated;

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshSources();
        _mixerService.StartLevelMonitoring();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _mixerService.StopLevelMonitoring();
        _mixerService.StopMonitoring();
        _mixerService.Dispose();
    }

    private void RefreshSources()
    {
        var sources = _mixerService.GetAudioSources();
        ChannelList.ItemsSource = sources;
    }

    private void OnSourcesUpdated(object? sender, List<AudioSource> sources)
    {
        Dispatcher.Invoke(() =>
        {
            // Update existing sources with new levels
            var currentSources = ChannelList.ItemsSource as List<AudioSource>;
            if (currentSources != null)
            {
                foreach (var source in sources)
                {
                    var existing = currentSources.FirstOrDefault(s => s.Id == source.Id);
                    if (existing != null)
                    {
                        existing.PeakLevel = source.PeakLevel;
                        existing.Volume = source.Volume;
                        existing.IsMuted = source.IsMuted;
                    }
                }
            }
            // Refresh binding
            ChannelList.Items.Refresh();
        });
    }

    private void OnLevelUpdated(object? sender, LevelUpdateEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Update master level bar
            var levelWidth = e.MasterLevel * (MasterLevelBar.Parent as FrameworkElement)?.ActualWidth ?? 0;
            MasterLevelBar.Width = Math.Max(0, levelWidth);

            // Calculate dB level for display
            var db = e.MasterLevel > 0 ? 20 * Math.Log10(e.MasterLevel) : -60;
            MasterLevelText.Text = $"{db:F0} dB";

            // Change color based on level
            if (e.MasterLevel > 0.9)
            {
                MasterLevelBar.SetResourceReference(BackgroundProperty, "NeonPinkBrush");
            }
            else if (e.MasterLevel > 0.7)
            {
                MasterLevelBar.SetResourceReference(BackgroundProperty, "NeonBlueBrush");
            }
            else
            {
                MasterLevelBar.SetResourceReference(BackgroundProperty, "NeonGreenBrush");
            }
        });
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is Slider slider && slider.Tag is AudioSource source)
        {
            _mixerService.SetSourceVolume(source, (float)e.NewValue);
        }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggle && toggle.Tag is AudioSource source)
        {
            _mixerService.SetSourceMute(source, toggle.IsChecked ?? false);
        }
    }

    private void MonitorToggle_Click(object sender, RoutedEventArgs e)
    {
        if (MonitorToggle.IsChecked == true)
        {
            try
            {
                _mixerService.StartMonitoring();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not start monitoring: {ex.Message}\n\n" +
                    "Make sure a virtual cable (like VB-Cable) is installed.",
                    "Monitor Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
            }
        }
        else
        {
            _mixerService.StopMonitoring();
        }
    }
}
