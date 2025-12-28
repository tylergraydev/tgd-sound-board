using System.Windows;
using System.Windows.Controls;
using TgdSoundboard.Services;

namespace TgdSoundboard.Views;

public partial class AppRoutingWindow : Window
{
    private List<AudioApp>? _apps;
    private HashSet<string> _addedSources = new(StringComparer.OrdinalIgnoreCase);
    private bool _soundboardAdded = false;

    public AppRoutingWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateConnectionStatus();
        LoadApps();

        // Subscribe to connection changes
        App.Streamlabs.ConnectionChanged += OnConnectionChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Streamlabs.ConnectionChanged -= OnConnectionChanged;
        base.OnClosed(e);
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        Dispatcher.Invoke(UpdateConnectionStatus);
    }

    private void UpdateConnectionStatus()
    {
        ConnectionStatus.Text = App.Streamlabs.IsConnected ? "Connected" : "Not Connected";
        AddSoundboardButton.IsEnabled = App.Streamlabs.IsConnected;
    }

    private void LoadApps()
    {
        _apps = AppAudioService.GetRunningAudioApps();

        // Filter out the soundboard itself
        var currentProcessId = Environment.ProcessId;
        _apps = _apps.Where(a => a.ProcessId != currentProcessId).ToList();

        AppList.ItemsSource = _apps;
        EmptyState.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadApps();
    }

    private async void AddSoundboard_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Streamlabs.IsConnected)
        {
            MessageBox.Show(
                "Not connected to Streamlabs Desktop.\n\nGo to Streamlabs Settings to configure the connection.",
                "Not Connected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AddSoundboardButton.IsEnabled = false;

        try
        {
            var success = await App.Streamlabs.AddSoundboardAudioSourceAsync();

            if (success)
            {
                _soundboardAdded = true;
                UpdateSoundboardButtonState();
            }
            else
            {
                MessageBox.Show(
                    $"Failed to add audio source.\n\n{App.Streamlabs.LastError}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                AddSoundboardButton.IsEnabled = App.Streamlabs.IsConnected;
            }
        }
        catch
        {
            AddSoundboardButton.IsEnabled = App.Streamlabs.IsConnected;
        }
    }

    private void UpdateSoundboardButtonState()
    {
        if (_soundboardAdded)
        {
            AddSoundboardButton.IsEnabled = false;
            AddSoundboardButton.Content = CreateAddedContent();
        }
    }

    private StackPanel CreateAddedContent()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new MaterialDesignThemes.Wpf.PackIcon
        {
            Kind = MaterialDesignThemes.Wpf.PackIconKind.Check,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });
        panel.Children.Add(new TextBlock { Text = "ADDED", FontWeight = FontWeights.Bold });
        return panel;
    }

    private async void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Streamlabs.IsConnected)
        {
            MessageBox.Show(
                "Not connected to Streamlabs Desktop.\n\nGo to Streamlabs Settings to configure the connection.",
                "Not Connected",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (sender is Button button && button.Tag is string processName)
        {
            button.IsEnabled = false;

            try
            {
                var success = await App.Streamlabs.AddApplicationAudioCaptureAsync(processName);

                if (success)
                {
                    _addedSources.Add(processName);
                    button.Content = CreateAddedContent();
                    // Keep button disabled to show it's added
                }
                else
                {
                    MessageBox.Show(
                        $"Failed to add audio source.\n\n{App.Streamlabs.LastError}",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    button.IsEnabled = true;
                }
            }
            catch
            {
                button.IsEnabled = true;
            }
        }
    }
}
