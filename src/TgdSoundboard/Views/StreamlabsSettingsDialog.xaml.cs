using System.Windows;
using System.Windows.Media;

namespace TgdSoundboard.Views;

public partial class StreamlabsSettingsDialog : Window
{
    public string Token { get; private set; } = string.Empty;
    public bool AutoConnect { get; private set; }
    public bool WasSaved { get; private set; }

    public StreamlabsSettingsDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var settings = App.MainViewModel.Settings;
        TokenTextBox.Text = settings.StreamlabsToken;
        AutoConnectCheckBox.IsChecked = settings.StreamlabsAutoConnect;

        UpdateConnectionStatus();

        // Subscribe to connection changes
        App.Streamlabs.ConnectionChanged += OnConnectionChanged;
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Auto-save settings when dialog is closing (unless already saved via Save button)
        if (!WasSaved)
        {
            var token = TokenTextBox.Text.Trim();
            var autoConnect = AutoConnectCheckBox.IsChecked ?? false;
            await App.MainViewModel.SaveStreamlabsSettingsAsync(token, autoConnect, string.Empty);
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        App.Streamlabs.ConnectionChanged -= OnConnectionChanged;
        base.OnClosed(e);
    }

    private void OnConnectionChanged(object? sender, bool isConnected)
    {
        Dispatcher.Invoke(UpdateConnectionStatus);
    }

    private void UpdateConnectionStatus()
    {
        if (App.Streamlabs.IsConnected)
        {
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            StatusText.Text = "Connected to Streamlabs Desktop";
        }
        else
        {
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
            StatusText.Text = App.Streamlabs.LastError ?? "Not connected";
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenTextBox.Text.Trim();
        if (string.IsNullOrEmpty(token))
        {
            StatusText.Text = "Please enter a token first";
            return;
        }

        TestButton.IsEnabled = false;
        StatusText.Text = "Connecting...";
        StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange

        App.Streamlabs.SetToken(token);
        var success = await App.Streamlabs.ConnectAsync();

        TestButton.IsEnabled = true;
        UpdateConnectionStatus();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        Token = TokenTextBox.Text.Trim();
        AutoConnect = AutoConnectCheckBox.IsChecked ?? false;
        WasSaved = true;

        await App.MainViewModel.SaveStreamlabsSettingsAsync(Token, AutoConnect, string.Empty);

        DialogResult = true;
        Close();
    }
}
