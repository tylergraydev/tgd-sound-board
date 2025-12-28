using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TgdSoundboard.Models;

namespace TgdSoundboard.Services;

public class AudioPlaybackService : IDisposable
{
    private readonly Dictionary<int, PlaybackInstance> _activePlaybacks = new();
    private readonly MixingSampleProvider _mixer;
    private readonly WasapiOut _outputDevice;
    private readonly object _lock = new();
    private float _masterVolume = 1.0f;

    public event EventHandler<int>? ClipStarted;
    public event EventHandler<int>? ClipStopped;

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            lock (_lock)
            {
                foreach (var playback in _activePlaybacks.Values)
                {
                    playback.UpdateVolume(_masterVolume);
                }
            }
        }
    }

    public AudioPlaybackService(string? deviceId = null)
    {
        var device = GetOutputDevice(deviceId);
        _outputDevice = device != null
            ? new WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 100)
            : new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);

        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
        {
            ReadFully = true
        };

        _outputDevice.Init(_mixer);
        _outputDevice.Play();
    }

    public void PlayClip(SoundClip clip)
    {
        lock (_lock)
        {
            // Stop if already playing
            if (_activePlaybacks.ContainsKey(clip.Id))
            {
                StopClip(clip.Id);
            }

            try
            {
                var reader = new AudioFileReader(clip.FilePath);
                var volumeProvider = new VolumeSampleProvider(reader)
                {
                    Volume = clip.Volume * _masterVolume
                };

                var playback = new PlaybackInstance(clip.Id, reader, volumeProvider, clip.Volume);
                _activePlaybacks[clip.Id] = playback;

                _mixer.AddMixerInput(volumeProvider);
                clip.IsPlaying = true;
                ClipStarted?.Invoke(this, clip.Id);

                // Monitor for completion
                _ = MonitorPlaybackAsync(clip);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error playing clip: {ex.Message}");
            }
        }
    }

    private async Task MonitorPlaybackAsync(SoundClip clip)
    {
        PlaybackInstance? playback;
        lock (_lock)
        {
            _activePlaybacks.TryGetValue(clip.Id, out playback);
        }

        if (playback == null) return;

        while (playback.Reader.Position < playback.Reader.Length)
        {
            await Task.Delay(100);

            lock (_lock)
            {
                if (!_activePlaybacks.ContainsKey(clip.Id))
                    return; // Was stopped externally
            }
        }

        StopClip(clip.Id);
    }

    public void StopClip(int clipId)
    {
        lock (_lock)
        {
            if (_activePlaybacks.TryGetValue(clipId, out var playback))
            {
                _mixer.RemoveMixerInput(playback.VolumeProvider);
                playback.Dispose();
                _activePlaybacks.Remove(clipId);
                ClipStopped?.Invoke(this, clipId);
            }
        }
    }

    public void StopAll()
    {
        lock (_lock)
        {
            var clipIds = _activePlaybacks.Keys.ToList();
            foreach (var clipId in clipIds)
            {
                StopClip(clipId);
            }
        }
    }

    public bool IsPlaying(int clipId)
    {
        lock (_lock)
        {
            return _activePlaybacks.ContainsKey(clipId);
        }
    }

    public void SetClipVolume(int clipId, float volume)
    {
        lock (_lock)
        {
            if (_activePlaybacks.TryGetValue(clipId, out var playback))
            {
                playback.ClipVolume = volume;
                playback.UpdateVolume(_masterVolume);
            }
        }
    }

    public static TimeSpan GetAudioDuration(string filePath)
    {
        try
        {
            using var reader = new AudioFileReader(filePath);
            return reader.TotalTime;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    public static List<AudioDevice> GetOutputDevices()
    {
        var devices = new List<AudioDevice>();
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.DeviceState.Active))
        {
            var isVirtualCable = device.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase) ||
                                 device.FriendlyName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                                 device.FriendlyName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase);

            devices.Add(new AudioDevice
            {
                Id = device.ID,
                Name = device.FriendlyName,
                DeviceType = AudioDeviceType.Output,
                IsDefault = device.ID == enumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia).ID,
                IsVirtualCable = isVirtualCable
            });
        }

        return devices;
    }

    public static List<AudioDevice> GetInputDevices()
    {
        var devices = new List<AudioDevice>();
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();

        foreach (var device in enumerator.EnumerateAudioEndPoints(
            NAudio.CoreAudioApi.DataFlow.Capture,
            NAudio.CoreAudioApi.DeviceState.Active))
        {
            devices.Add(new AudioDevice
            {
                Id = device.ID,
                Name = device.FriendlyName,
                DeviceType = AudioDeviceType.Input,
                IsDefault = device.ID == enumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Capture,
                    NAudio.CoreAudioApi.Role.Multimedia).ID
            });
        }

        return devices;
    }

    private static NAudio.CoreAudioApi.MMDevice? GetOutputDevice(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return null;

        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        try
        {
            return enumerator.GetDevice(deviceId);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        StopAll();
        _outputDevice.Stop();
        _outputDevice.Dispose();
    }

    private class PlaybackInstance : IDisposable
    {
        public int ClipId { get; }
        public AudioFileReader Reader { get; }
        public VolumeSampleProvider VolumeProvider { get; }
        public float ClipVolume { get; set; }

        public PlaybackInstance(int clipId, AudioFileReader reader, VolumeSampleProvider volumeProvider, float clipVolume)
        {
            ClipId = clipId;
            Reader = reader;
            VolumeProvider = volumeProvider;
            ClipVolume = clipVolume;
        }

        public void UpdateVolume(float masterVolume)
        {
            VolumeProvider.Volume = ClipVolume * masterVolume;
        }

        public void Dispose()
        {
            Reader.Dispose();
        }
    }
}
