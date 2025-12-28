using System.Diagnostics;
using System.Reflection;
using System.Windows;
using AutoUpdaterDotNET;

namespace TgdSoundboard.Services;

public class UpdateService
{
    // This URL should point to your update.xml file hosted on GitHub or a web server
    private const string UpdateUrl = "https://raw.githubusercontent.com/tylergraydev/tgd-sound-board/main/update.xml";

    public static void Initialize()
    {
        // Configure AutoUpdater
        AutoUpdater.Synchronous = false;
        AutoUpdater.ShowSkipButton = true;
        AutoUpdater.ShowRemindLaterButton = true;
        AutoUpdater.LetUserSelectRemindLater = true;
        AutoUpdater.RemindLaterTimeSpan = RemindLaterFormat.Days;
        AutoUpdater.RemindLaterAt = 1;

        // Customize the update dialog
        AutoUpdater.AppTitle = "TGD Soundboard";

        // Check for updates on startup
        AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;
    }

    public static void CheckForUpdates(bool silent = true)
    {
        if (silent)
        {
            AutoUpdater.Start(UpdateUrl);
        }
        else
        {
            // Show dialog even if no update available
            AutoUpdater.ReportErrors = true;
            AutoUpdater.Start(UpdateUrl);
        }
    }

    private static void OnCheckForUpdate(UpdateInfoEventArgs args)
    {
        if (args.Error == null)
        {
            if (args.IsUpdateAvailable)
            {
                var result = MessageBox.Show(
                    $"A new version ({args.CurrentVersion}) is available!\n\n" +
                    $"You are currently running version {args.InstalledVersion}.\n\n" +
                    $"Do you want to update now?",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (AutoUpdater.DownloadUpdate(args))
                        {
                            Application.Current.Shutdown();
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            $"Error downloading update: {ex.Message}",
                            "Update Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    }
                }
            }
        }
        else
        {
            if (!AutoUpdater.ReportErrors)
            {
                // Silent check, log error but don't show dialog
                Debug.WriteLine($"Update check error: {args.Error.Message}");
            }
        }
    }

    public static string GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
    }
}
