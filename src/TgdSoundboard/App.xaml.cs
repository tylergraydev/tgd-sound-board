using System.Windows;
using TgdSoundboard.Services;
using TgdSoundboard.ViewModels;

namespace TgdSoundboard;

public partial class App : Application
{
    public static DatabaseService Database { get; private set; } = null!;
    public static AudioPlaybackService AudioPlayback { get; private set; } = null!;
    public static AudioRouterService AudioRouter { get; private set; } = null!;
    public static ClipStorageService ClipStorage { get; private set; } = null!;
    public static MainViewModel MainViewModel { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize services
        Database = new DatabaseService();
        var settings = await Database.GetAppSettingsAsync();

        ClipStorage = new ClipStorageService(settings.ClipsDirectory);
        AudioPlayback = new AudioPlaybackService(settings.OutputDeviceId);
        AudioRouter = new AudioRouterService();

        // Initialize main view model
        MainViewModel = new MainViewModel();
        await MainViewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AudioPlayback?.Dispose();
        AudioRouter?.Dispose();
        base.OnExit(e);
    }
}
