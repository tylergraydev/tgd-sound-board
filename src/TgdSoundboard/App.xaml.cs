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
    public static GlobalHotkeyService GlobalHotkeys { get; private set; } = null!;
    public static MainViewModel MainViewModel { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Initialize update service and check for updates
        UpdateService.Initialize();
        UpdateService.CheckForUpdates(silent: true);

        // Initialize services
        Database = new DatabaseService();
        var settings = await Database.GetAppSettingsAsync();

        // Apply saved theme if available (after app resources are loaded)
        if (!string.IsNullOrEmpty(settings.Theme) && Enum.TryParse<AppTheme>(settings.Theme, out var savedTheme))
        {
            ThemeService.CurrentTheme = savedTheme;
        }

        ClipStorage = new ClipStorageService(settings.ClipsDirectory);
        AudioPlayback = new AudioPlaybackService(settings.OutputDeviceId, settings.VirtualCableDeviceId);
        AudioRouter = new AudioRouterService();
        GlobalHotkeys = new GlobalHotkeyService();

        // Initialize main view model
        MainViewModel = new MainViewModel();
        await MainViewModel.InitializeAsync();

        // Initialize global hotkeys after window is loaded
        if (MainWindow != null)
        {
            MainWindow.Loaded += (s, args) =>
            {
                GlobalHotkeys.Initialize((Window)MainWindow);
                RegisterSavedHotkeys();
            };
        }
    }

    private void RegisterSavedHotkeys()
    {
        foreach (var category in MainViewModel.Categories)
        {
            foreach (var clip in category.Clips)
            {
                if (!string.IsNullOrEmpty(clip.Hotkey))
                {
                    var capturedClip = clip;
                    GlobalHotkeys.RegisterHotkey(clip.Hotkey, () =>
                    {
                        Dispatcher.Invoke(() => MainViewModel.PlayClipCommand.Execute(capturedClip));
                    });
                }
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GlobalHotkeys?.Dispose();
        AudioPlayback?.Dispose();
        AudioRouter?.Dispose();
        base.OnExit(e);
    }
}
