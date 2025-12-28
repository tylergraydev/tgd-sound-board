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
            var deviceEnumerator = new MMDeviceEnumerator();
            var device = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = device.AudioSessionManager;

            for (int i = 0; i < sessionManager.Sessions.Count; i++)
            {
                var session = sessionManager.Sessions[i];
                var processId = (int)session.GetProcessID;

                if (processId == 0) continue;

                try
                {
                    var process = Process.GetProcessById(processId);
                    var app = new AudioApp
                    {
                        ProcessId = processId,
                        ProcessName = process.ProcessName,
                        DisplayName = GetDisplayName(session, process),
                        Volume = session.SimpleAudioVolume.Volume,
                        IsMuted = session.SimpleAudioVolume.Mute,
                        IconPath = GetProcessIconPath(process)
                    };
                    apps.Add(app);
                }
                catch
                {
                    // Process may have exited
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting audio apps: {ex.Message}");
        }

        return apps.DistinctBy(a => a.ProcessId).OrderBy(a => a.DisplayName).ToList();
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

public class AudioApp
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public float Volume { get; set; } = 1.0f;
    public bool IsMuted { get; set; }
    public string IconPath { get; set; } = string.Empty;
    public bool RouteToVirtualCable { get; set; }
}
