using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace TgdSoundboard.Services;

public class AudioMixerService : IDisposable
{
    private WasapiLoopbackCapture? _monitorCapture;
    private WaveOutEvent? _monitorOutput;
    private BufferedWaveProvider? _monitorBuffer;
    private readonly System.Timers.Timer _levelTimer;
    private bool _isMonitoring;

    public event EventHandler<LevelUpdateEventArgs>? LevelUpdated;
    public event EventHandler<List<AudioSource>>? SourcesUpdated;

    public bool IsMonitoring => _isMonitoring;

    public AudioMixerService()
    {
        _levelTimer = new System.Timers.Timer(50); // 20fps update
        _levelTimer.Elapsed += OnLevelTimerElapsed;
    }

    public void StartLevelMonitoring()
    {
        _levelTimer.Start();
    }

    public void StopLevelMonitoring()
    {
        _levelTimer.Stop();
    }

    private void OnLevelTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        try
        {
            var sources = GetAudioSources();
            SourcesUpdated?.Invoke(this, sources);

            // Get master output level
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var masterLevel = device.AudioMeterInformation.MasterPeakValue;

            LevelUpdated?.Invoke(this, new LevelUpdateEventArgs
            {
                MasterLevel = masterLevel,
                Sources = sources
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Level monitoring error: {ex.Message}");
        }
    }

    public List<AudioSource> GetAudioSources()
    {
        var sources = new List<AudioSource>();

        try
        {
            var enumerator = new MMDeviceEnumerator();

            // Get microphone
            try
            {
                var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                sources.Add(new AudioSource
                {
                    Id = mic.ID,
                    Name = "Microphone",
                    DisplayName = mic.FriendlyName,
                    Type = AudioSourceType.Microphone,
                    Volume = mic.AudioEndpointVolume.MasterVolumeLevelScalar,
                    IsMuted = mic.AudioEndpointVolume.Mute,
                    PeakLevel = mic.AudioMeterInformation.MasterPeakValue,
                    IconKind = "Microphone"
                });
            }
            catch { }

            // Get system audio (output device for level)
            try
            {
                var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

                // Add system master
                sources.Add(new AudioSource
                {
                    Id = "system_master",
                    Name = "System Audio",
                    DisplayName = output.FriendlyName,
                    Type = AudioSourceType.System,
                    Volume = output.AudioEndpointVolume.MasterVolumeLevelScalar,
                    IsMuted = output.AudioEndpointVolume.Mute,
                    PeakLevel = output.AudioMeterInformation.MasterPeakValue,
                    IconKind = "VolumeHigh"
                });

                // Get individual app sessions
                var sessionManager = output.AudioSessionManager;
                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    var processId = (int)session.GetProcessID;

                    if (processId == 0) continue;

                    try
                    {
                        var process = Process.GetProcessById(processId);
                        var displayName = !string.IsNullOrEmpty(session.DisplayName) && !session.DisplayName.StartsWith("@")
                            ? session.DisplayName
                            : (process.MainWindowTitle.Length > 0 ? process.MainWindowTitle : process.ProcessName);

                        sources.Add(new AudioSource
                        {
                            Id = $"app_{processId}",
                            Name = process.ProcessName,
                            DisplayName = displayName,
                            ProcessId = processId,
                            Type = AudioSourceType.Application,
                            Volume = session.SimpleAudioVolume.Volume,
                            IsMuted = session.SimpleAudioVolume.Mute,
                            PeakLevel = session.AudioMeterInformation.MasterPeakValue,
                            IconKind = GetAppIcon(process.ProcessName)
                        });
                    }
                    catch { }
                }
            }
            catch { }

            // Check for virtual cable
            try
            {
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        sources.Add(new AudioSource
                        {
                            Id = device.ID,
                            Name = "Virtual Cable",
                            DisplayName = device.FriendlyName,
                            Type = AudioSourceType.VirtualCable,
                            Volume = device.AudioEndpointVolume.MasterVolumeLevelScalar,
                            IsMuted = device.AudioEndpointVolume.Mute,
                            PeakLevel = device.AudioMeterInformation.MasterPeakValue,
                            IconKind = "CableData"
                        });
                        break;
                    }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting audio sources: {ex.Message}");
        }

        return sources;
    }

    private string GetAppIcon(string processName)
    {
        return processName.ToLower() switch
        {
            "spotify" => "Spotify",
            "discord" => "MessageVideo",
            "chrome" or "msedge" or "firefox" => "Web",
            "obs64" or "obs" => "Video",
            "steam" or "steamwebhelper" => "Steam",
            _ => "Application"
        };
    }

    public void SetSourceVolume(AudioSource source, float volume)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();

            if (source.Type == AudioSourceType.Microphone)
            {
                var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                mic.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
            else if (source.Type == AudioSourceType.System)
            {
                var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                output.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
            else if (source.Type == AudioSourceType.Application && source.ProcessId > 0)
            {
                var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = output.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    if (session.GetProcessID == source.ProcessId)
                    {
                        session.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                        break;
                    }
                }
            }
            else if (source.Type == AudioSourceType.VirtualCable)
            {
                var device = enumerator.GetDevice(source.Id);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error setting volume: {ex.Message}");
        }
    }

    public void SetSourceMute(AudioSource source, bool mute)
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();

            if (source.Type == AudioSourceType.Microphone)
            {
                var mic = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                mic.AudioEndpointVolume.Mute = mute;
            }
            else if (source.Type == AudioSourceType.System)
            {
                var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                output.AudioEndpointVolume.Mute = mute;
            }
            else if (source.Type == AudioSourceType.Application && source.ProcessId > 0)
            {
                var output = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessionManager = output.AudioSessionManager;

                for (int i = 0; i < sessionManager.Sessions.Count; i++)
                {
                    var session = sessionManager.Sessions[i];
                    if (session.GetProcessID == source.ProcessId)
                    {
                        session.SimpleAudioVolume.Mute = mute;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error setting mute: {ex.Message}");
        }
    }

    public void StartMonitoring(string? virtualCableDeviceId = null)
    {
        if (_isMonitoring) return;

        try
        {
            var enumerator = new MMDeviceEnumerator();

            // Find virtual cable to monitor
            MMDevice? virtualCable = null;
            if (!string.IsNullOrEmpty(virtualCableDeviceId))
            {
                virtualCable = enumerator.GetDevice(virtualCableDeviceId);
            }
            else
            {
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    if (device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                    {
                        virtualCable = device;
                        break;
                    }
                }
            }

            if (virtualCable == null)
            {
                throw new InvalidOperationException("Virtual cable not found");
            }

            // Capture from virtual cable
            _monitorCapture = new WasapiLoopbackCapture(virtualCable);

            _monitorBuffer = new BufferedWaveProvider(_monitorCapture.WaveFormat)
            {
                BufferLength = _monitorCapture.WaveFormat.AverageBytesPerSecond * 2,
                DiscardOnBufferOverflow = true
            };

            _monitorCapture.DataAvailable += (s, e) =>
            {
                _monitorBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            // Output to default speakers
            _monitorOutput = new WaveOutEvent();
            _monitorOutput.Init(_monitorBuffer);

            _monitorCapture.StartRecording();
            _monitorOutput.Play();

            _isMonitoring = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error starting monitor: {ex.Message}");
            StopMonitoring();
            throw;
        }
    }

    public void StopMonitoring()
    {
        _monitorCapture?.StopRecording();
        _monitorCapture?.Dispose();
        _monitorCapture = null;

        _monitorOutput?.Stop();
        _monitorOutput?.Dispose();
        _monitorOutput = null;

        _monitorBuffer = null;
        _isMonitoring = false;
    }

    public void Dispose()
    {
        StopLevelMonitoring();
        StopMonitoring();
        _levelTimer.Dispose();
    }
}

public class AudioSource
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public AudioSourceType Type { get; set; }
    public float Volume { get; set; }
    public bool IsMuted { get; set; }
    public float PeakLevel { get; set; }
    public string IconKind { get; set; } = "Application";
}

public enum AudioSourceType
{
    Microphone,
    System,
    Application,
    VirtualCable,
    Soundboard
}

public class LevelUpdateEventArgs : EventArgs
{
    public float MasterLevel { get; set; }
    public List<AudioSource> Sources { get; set; } = new();
}
