using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using TgdSoundboard.Models;

namespace TgdSoundboard.Services;

public class AppAudioService
{
    public static List<AudioApp> GetRunningAudioApps()
    {
        var apps = new List<AudioApp>();

        try
        {
            // Get all processes with a main window (visible apps)
            var processes = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle) || IsKnownAudioApp(p.ProcessName))
                .ToList();

            foreach (var process in processes)
            {
                try
                {
                    var app = new AudioApp
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        DisplayName = !string.IsNullOrEmpty(process.MainWindowTitle)
                            ? process.MainWindowTitle
                            : process.ProcessName,
                        Volume = 1.0f,
                        IsMuted = false,
                        IconPath = GetProcessIconPath(process)
                    };
                    apps.Add(app);
                }
                catch
                {
                    // Process may have exited or access denied
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting apps: {ex.Message}");
        }

        return apps.DistinctBy(a => a.ProcessId).OrderBy(a => a.DisplayName).ToList();
    }

    private static bool IsKnownAudioApp(string processName)
    {
        // Common audio apps that might not have a window title
        var knownApps = new[]
        {
            "spotify", "discord", "chrome", "firefox", "msedge", "brave",
            "vlc", "foobar2000", "winamp", "itunes", "musicbee",
            "obs64", "obs32", "streamlabs", "slack", "teams", "zoom"
        };
        return knownApps.Any(a => processName.Equals(a, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDisplayName(AudioSessionControl session, Process process)
    {
        var displayName = session.DisplayName;
        if (!string.IsNullOrEmpty(displayName) && !displayName.StartsWith("@"))
            return displayName;

        try
        {
            return process.MainWindowTitle.Length > 0 ? process.MainWindowTitle : process.ProcessName;
        }
        catch
        {
            return process.ProcessName;
        }
    }

    private static string GetProcessIconPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void SetAppVolume(int processId, float volume)
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = device.AudioSessionManager;

            for (int i = 0; i < sessionManager.Sessions.Count; i++)
            {
                var session = sessionManager.Sessions[i];
                if (session.GetProcessID == processId)
                {
                    session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error setting app volume: {ex.Message}");
        }
    }

    public static void SetAppMute(int processId, bool mute)
    {
        try
        {
            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = device.AudioSessionManager;

            for (int i = 0; i < sessionManager.Sessions.Count; i++)
            {
                var session = sessionManager.Sessions[i];
                if (session.GetProcessID == processId)
                {
                    session.SimpleAudioVolume.Mute = mute;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error setting app mute: {ex.Message}");
        }
    }
}

public class AudioApp : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    private float _volume = 1.0f;
    public float Volume
    {
        get => _volume;
        set => SetProperty(ref _volume, value);
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set => SetProperty(ref _isMuted, value);
    }

    public string IconPath { get; set; } = string.Empty;

    private bool _isRouted;
    public bool IsRouted
    {
        get => _isRouted;
        set => SetProperty(ref _isRouted, value);
    }
}
