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
    private readonly Queue<SoundClip> _clipQueue = new();
    private readonly object _queueLock = new();

    // Monitor output (local preview)
    private WasapiOut? _monitorDevice;
    private MixingSampleProvider? _monitorMixer;
    private bool _isMonitorEnabled;
    private string? _monitorDeviceId;

    // App audio routing (capture system audio and mix into output)
    private WasapiLoopbackCapture? _systemAudioCapture;
    private BufferedWaveProvider? _systemAudioBuffer;
    private ISampleProvider? _systemAudioProvider;
    private bool _isAppRoutingEnabled;
    private readonly HashSet<int> _routedAppProcessIds = new();
    private readonly object _routingLock = new();

    public event EventHandler<int>? ClipStarted;
    public event EventHandler<int>? ClipStopped;
    public event EventHandler? QueueChanged;
    public event EventHandler? RoutedAppsChanged;

    public bool IsMonitorEnabled => _isMonitorEnabled;
    public bool IsAppRoutingEnabled => _isAppRoutingEnabled;
    public IReadOnlySet<int> RoutedAppProcessIds => _routedAppProcessIds;

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
                ISampleProvider sampleProvider = reader;

                // Convert to mixer format (44100Hz stereo) if needed
                sampleProvider = ConvertToMixerFormat(sampleProvider);

                // Apply speed change if configured
                if (Math.Abs(clip.PlaybackSpeed - 1.0f) > 0.01f)
                {
                    sampleProvider = new SpeedControlSampleProvider(sampleProvider, clip.PlaybackSpeed);
                }

                // Apply pitch shift if configured
                if (clip.PitchSemitones != 0)
                {
                    sampleProvider = new PitchShiftSampleProvider(sampleProvider, clip.PitchSemitones);
                }

                // Apply fade in if configured
                if (clip.FadeInSeconds > 0)
                {
                    sampleProvider = new FadeInOutSampleProvider(sampleProvider);
                    ((FadeInOutSampleProvider)sampleProvider).BeginFadeIn(clip.FadeInSeconds * 1000);
                }

                var volumeProvider = new VolumeSampleProvider(sampleProvider)
                {
                    Volume = clip.Volume * _masterVolume
                };

                var playback = new PlaybackInstance(clip.Id, reader, volumeProvider, clip.Volume, clip.IsLooping, clip.FadeOutSeconds);
                _activePlaybacks[clip.Id] = playback;

                _mixer.AddMixerInput(volumeProvider);

                // Also send to monitor if enabled
                if (_isMonitorEnabled && _monitorMixer != null)
                {
                    try
                    {
                        var monitorReader = new AudioFileReader(clip.FilePath);
                        ISampleProvider monitorProvider = monitorReader;

                        // Convert to mixer format
                        monitorProvider = ConvertToMixerFormat(monitorProvider);

                        if (Math.Abs(clip.PlaybackSpeed - 1.0f) > 0.01f)
                        {
                            monitorProvider = new SpeedControlSampleProvider(monitorProvider, clip.PlaybackSpeed);
                        }

                        if (clip.PitchSemitones != 0)
                        {
                            monitorProvider = new PitchShiftSampleProvider(monitorProvider, clip.PitchSemitones);
                        }

                        if (clip.FadeInSeconds > 0)
                        {
                            monitorProvider = new FadeInOutSampleProvider(monitorProvider);
                            ((FadeInOutSampleProvider)monitorProvider).BeginFadeIn(clip.FadeInSeconds * 1000);
                        }

                        var monitorVolumeProvider = new VolumeSampleProvider(monitorProvider)
                        {
                            Volume = clip.Volume * _masterVolume
                        };

                        playback.MonitorReader = monitorReader;
                        playback.MonitorVolumeProvider = monitorVolumeProvider;
                        _monitorMixer.AddMixerInput(monitorVolumeProvider);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error adding to monitor: {ex.Message}");
                    }
                }

                // Monitor for completion
                _ = MonitorPlaybackAsync(clip);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception playing clip {clip.Id} ({clip.Name}): {ex.GetType().Name}: {ex.Message}");
                return;
            }
        }

        // Fire event and set state outside lock to avoid deadlock
        clip.IsPlaying = true;
        ClipStarted?.Invoke(this, clip.Id);
    }

    #region Queue Management

    public IReadOnlyList<SoundClip> GetQueue()
    {
        lock (_queueLock)
        {
            return _clipQueue.ToList();
        }
    }

    public void AddToQueue(SoundClip clip)
    {
        lock (_queueLock)
        {
            _clipQueue.Enqueue(clip);
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearQueue()
    {
        lock (_queueLock)
        {
            _clipQueue.Clear();
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RemoveFromQueue(SoundClip clip)
    {
        lock (_queueLock)
        {
            var items = _clipQueue.ToList();
            items.Remove(clip);
            _clipQueue.Clear();
            foreach (var item in items)
            {
                _clipQueue.Enqueue(item);
            }
        }
        QueueChanged?.Invoke(this, EventArgs.Empty);
    }

    public SoundClip? PlayNextInQueue()
    {
        SoundClip? nextClip = null;
        lock (_queueLock)
        {
            if (_clipQueue.Count > 0)
            {
                nextClip = _clipQueue.Dequeue();
            }
        }

        if (nextClip != null)
        {
            QueueChanged?.Invoke(this, EventArgs.Empty);
            PlayClip(nextClip);
        }

        return nextClip;
    }

    public int QueueCount
    {
        get
        {
            lock (_queueLock)
            {
                return _clipQueue.Count;
            }
        }
    }

    #endregion

    private async Task MonitorPlaybackAsync(SoundClip clip)
    {
        PlaybackInstance? playback;
        lock (_lock)
        {
            _activePlaybacks.TryGetValue(clip.Id, out playback);
        }

        if (playback == null) return;

        var totalDuration = playback.Reader.TotalTime;
        var startTime = DateTime.Now;
        Console.WriteLine($"[Monitor] Started monitoring clip {clip.Id} ({clip.Name}), Duration={totalDuration.TotalSeconds:F1}s, IsLooping={playback.IsLooping}");

        while (true)
        {
            await Task.Delay(100);

            lock (_lock)
            {
                if (!_activePlaybacks.ContainsKey(clip.Id))
                {
                    Console.WriteLine($"[Monitor] Clip {clip.Id} was stopped externally");
                    return; // Was stopped externally
                }
            }

            try
            {
                var elapsed = DateTime.Now - startTime;
                var remaining = totalDuration - elapsed;

                // Check if near end for fade out
                if (playback.FadeOutSeconds > 0 && remaining.TotalSeconds <= playback.FadeOutSeconds && !playback.FadeOutStarted)
                {
                    playback.FadeOutStarted = true;
                    _ = ApplyFadeOutAsync(playback);
                }

                // Check if playback finished (use time-based tracking since resampler buffers)
                if (elapsed >= totalDuration)
                {
                    Console.WriteLine($"[Monitor] Clip {clip.Id} reached end: elapsed={elapsed.TotalSeconds:F1}s, duration={totalDuration.TotalSeconds:F1}s, IsLooping={playback.IsLooping}");
                    if (playback.IsLooping)
                    {
                        Console.WriteLine($"[Monitor] Looping clip {clip.Id}");
                        playback.Reader.Position = 0;
                        playback.FadeOutStarted = false;
                        startTime = DateTime.Now; // Reset timer for loop
                    }
                    else
                    {
                        Console.WriteLine($"[Monitor] Clip {clip.Id} finished, stopping");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Monitor] Exception for clip {clip.Id}: {ex.Message}");
                break;
            }
        }

        StopClip(clip.Id);
    }

    private ISampleProvider ConvertToMixerFormat(ISampleProvider source)
    {
        // Target format: 44100Hz stereo (matches mixer)
        const int targetSampleRate = 44100;

        var result = source;

        // Convert mono to stereo if needed
        if (result.WaveFormat.Channels == 1)
        {
            result = new MonoToStereoSampleProvider(result);
        }

        // Resample if sample rate doesn't match
        if (result.WaveFormat.SampleRate != targetSampleRate)
        {
            result = new WdlResamplingSampleProvider(result, targetSampleRate);
        }

        return result;
    }

    private async Task ApplyFadeOutAsync(PlaybackInstance playback)
    {
        var steps = 20;
        var stepTime = (playback.FadeOutSeconds * 1000) / steps;
        var volumeStep = playback.ClipVolume / steps;

        for (int i = 0; i < steps; i++)
        {
            await Task.Delay((int)stepTime);
            playback.VolumeProvider.Volume = Math.Max(0, playback.VolumeProvider.Volume - (volumeStep * _masterVolume));
        }
    }

    public void StopClip(int clipId)
    {
        bool wasStopped = false;
        lock (_lock)
        {
            if (_activePlaybacks.TryGetValue(clipId, out var playback))
            {
                _mixer.RemoveMixerInput(playback.VolumeProvider);

                // Also remove from monitor if applicable
                if (playback.MonitorVolumeProvider != null && _monitorMixer != null)
                {
                    _monitorMixer.RemoveMixerInput(playback.MonitorVolumeProvider);
                }

                playback.Dispose();
                _activePlaybacks.Remove(clipId);
                wasStopped = true;
            }
        }

        // Fire event outside lock to avoid deadlock with Dispatcher.Invoke
        if (wasStopped)
        {
            ClipStopped?.Invoke(this, clipId);
        }
    }

    public void StopAll()
    {
        List<int> stoppedClipIds;
        lock (_lock)
        {
            stoppedClipIds = _activePlaybacks.Keys.ToList();
            foreach (var playback in _activePlaybacks.Values)
            {
                _mixer.RemoveMixerInput(playback.VolumeProvider);
                if (playback.MonitorVolumeProvider != null && _monitorMixer != null)
                {
                    _monitorMixer.RemoveMixerInput(playback.MonitorVolumeProvider);
                }
                playback.Dispose();
            }
            _activePlaybacks.Clear();
        }

        // Fire events outside lock
        foreach (var clipId in stoppedClipIds)
        {
            ClipStopped?.Invoke(this, clipId);
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
            devices.Add(new AudioDevice
            {
                Id = device.ID,
                Name = device.FriendlyName,
                DeviceType = AudioDeviceType.Output,
                IsDefault = device.ID == enumerator.GetDefaultAudioEndpoint(
                    NAudio.CoreAudioApi.DataFlow.Render,
                    NAudio.CoreAudioApi.Role.Multimedia).ID
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

    #region Monitor (Local Preview)

    public void EnableMonitor(string? deviceId = null)
    {
        if (_isMonitorEnabled) return;

        try
        {
            _monitorDeviceId = deviceId;
            var device = GetOutputDevice(deviceId);

            _monitorDevice = device != null
                ? new WasapiOut(device, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 100)
                : new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);

            _monitorMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
            {
                ReadFully = true
            };

            _monitorDevice.Init(_monitorMixer);
            _monitorDevice.Play();
            _isMonitorEnabled = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enabling monitor: {ex.Message}");
            DisableMonitor();
            throw;
        }
    }

    public void DisableMonitor()
    {
        _isMonitorEnabled = false;

        _monitorDevice?.Stop();
        _monitorDevice?.Dispose();
        _monitorDevice = null;
        _monitorMixer = null;
        _monitorDeviceId = null;
    }

    #endregion

    #region App Audio Routing

    public void EnableAppRouting()
    {
        if (_isAppRoutingEnabled) return;

        try
        {
            var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.Role.Multimedia);

            _systemAudioCapture = new WasapiLoopbackCapture(defaultDevice);

            _systemAudioBuffer = new BufferedWaveProvider(_systemAudioCapture.WaveFormat)
            {
                BufferLength = _systemAudioCapture.WaveFormat.AverageBytesPerSecond * 2,
                DiscardOnBufferOverflow = true
            };

            _systemAudioCapture.DataAvailable += (s, e) =>
            {
                _systemAudioBuffer?.AddSamples(e.Buffer, 0, e.BytesRecorded);
            };

            // Convert to mixer format (44100 Hz stereo)
            _systemAudioProvider = ConvertToMixerFormat(
                _systemAudioBuffer.ToSampleProvider(),
                _systemAudioCapture.WaveFormat);

            _mixer.AddMixerInput(_systemAudioProvider);
            _systemAudioCapture.StartRecording();
            _isAppRoutingEnabled = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error enabling app routing: {ex.Message}");
            DisableAppRouting();
            throw;
        }
    }

    public void DisableAppRouting()
    {
        if (_systemAudioProvider != null)
        {
            _mixer.RemoveMixerInput(_systemAudioProvider);
        }

        _systemAudioCapture?.StopRecording();
        _systemAudioCapture?.Dispose();
        _systemAudioCapture = null;
        _systemAudioBuffer = null;
        _systemAudioProvider = null;
        _isAppRoutingEnabled = false;

        lock (_routingLock)
        {
            _routedAppProcessIds.Clear();
        }
        RoutedAppsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AddRoutedApp(int processId)
    {
        lock (_routingLock)
        {
            if (_routedAppProcessIds.Add(processId))
            {
                RoutedAppsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public void RemoveRoutedApp(int processId)
    {
        lock (_routingLock)
        {
            if (_routedAppProcessIds.Remove(processId))
            {
                RoutedAppsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsAppRouted(int processId)
    {
        lock (_routingLock)
        {
            return _routedAppProcessIds.Contains(processId);
        }
    }

    private ISampleProvider ConvertToMixerFormat(ISampleProvider source, WaveFormat originalFormat)
    {
        ISampleProvider result = source;

        // Convert to stereo if mono
        if (originalFormat.Channels == 1)
        {
            result = new MonoToStereoSampleProvider(result);
        }

        // Resample if needed
        if (originalFormat.SampleRate != 44100)
        {
            result = new WdlResamplingSampleProvider(result, 44100);
        }

        return result;
    }

    #endregion

    public void Dispose()
    {
        StopAll();
        DisableAppRouting();
        DisableMonitor();
        _outputDevice.Stop();
        _outputDevice.Dispose();
    }

    private class PlaybackInstance : IDisposable
    {
        public int ClipId { get; }
        public AudioFileReader Reader { get; }
        public VolumeSampleProvider VolumeProvider { get; }
        public float ClipVolume { get; set; }
        public bool IsLooping { get; }
        public float FadeOutSeconds { get; }
        public bool FadeOutStarted { get; set; }

        // Monitor output
        public AudioFileReader? MonitorReader { get; set; }
        public VolumeSampleProvider? MonitorVolumeProvider { get; set; }

        public PlaybackInstance(int clipId, AudioFileReader reader, VolumeSampleProvider volumeProvider, float clipVolume, bool isLooping = false, float fadeOutSeconds = 0)
        {
            ClipId = clipId;
            Reader = reader;
            VolumeProvider = volumeProvider;
            ClipVolume = clipVolume;
            IsLooping = isLooping;
            FadeOutSeconds = fadeOutSeconds;
        }

        public void UpdateVolume(float masterVolume)
        {
            VolumeProvider.Volume = ClipVolume * masterVolume;
            if (MonitorVolumeProvider != null)
            {
                MonitorVolumeProvider.Volume = ClipVolume * masterVolume;
            }
        }

        public void Dispose()
        {
            Reader.Dispose();
            MonitorReader?.Dispose();
        }
    }
}

/// <summary>
/// Simple speed control using linear interpolation
/// </summary>
public class SpeedControlSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _speed;
    private readonly float[] _sourceBuffer;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SpeedControlSampleProvider(ISampleProvider source, float speed)
    {
        _source = source;
        _speed = Math.Clamp(speed, 0.5f, 2.0f);
        _sourceBuffer = new float[_source.WaveFormat.SampleRate * _source.WaveFormat.Channels];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesNeeded = (int)(count * _speed) + 2;
        if (samplesNeeded > _sourceBuffer.Length)
        {
            samplesNeeded = _sourceBuffer.Length;
        }

        int sourceSamplesRead = _source.Read(_sourceBuffer, 0, samplesNeeded);
        if (sourceSamplesRead == 0) return 0;

        int outputSamples = 0;
        float sourcePosition = 0;

        while (outputSamples < count && sourcePosition < sourceSamplesRead - 1)
        {
            int index = (int)sourcePosition;
            float fraction = sourcePosition - index;

            if (index + 1 < sourceSamplesRead)
            {
                buffer[offset + outputSamples] = _sourceBuffer[index] * (1 - fraction) + _sourceBuffer[index + 1] * fraction;
            }
            else
            {
                buffer[offset + outputSamples] = _sourceBuffer[index];
            }

            outputSamples++;
            sourcePosition += _speed;
        }

        return outputSamples;
    }
}

/// <summary>
/// Simple pitch shift using resampling (changes tempo too - for true pitch shift without tempo change, use SoundTouch)
/// </summary>
public class PitchShiftSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float _pitchFactor;
    private readonly float[] _sourceBuffer;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public PitchShiftSampleProvider(ISampleProvider source, int semitones)
    {
        _source = source;
        _pitchFactor = (float)Math.Pow(2, semitones / 12.0);
        _sourceBuffer = new float[_source.WaveFormat.SampleRate * _source.WaveFormat.Channels * 2];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesNeeded = (int)(count * _pitchFactor) + 2;
        if (samplesNeeded > _sourceBuffer.Length)
        {
            samplesNeeded = _sourceBuffer.Length;
        }

        int sourceSamplesRead = _source.Read(_sourceBuffer, 0, samplesNeeded);
        if (sourceSamplesRead == 0) return 0;

        int outputSamples = 0;
        float sourcePosition = 0;

        while (outputSamples < count && sourcePosition < sourceSamplesRead - 1)
        {
            int index = (int)sourcePosition;
            float fraction = sourcePosition - index;

            if (index + 1 < sourceSamplesRead)
            {
                buffer[offset + outputSamples] = _sourceBuffer[index] * (1 - fraction) + _sourceBuffer[index + 1] * fraction;
            }
            else
            {
                buffer[offset + outputSamples] = _sourceBuffer[index];
            }

            outputSamples++;
            sourcePosition += _pitchFactor;
        }

        return outputSamples;
    }
}
