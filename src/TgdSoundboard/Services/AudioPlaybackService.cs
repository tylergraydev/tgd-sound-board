using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TgdSoundboard.Models;

namespace TgdSoundboard.Services;

public class AudioPlaybackService : IDisposable
{
    private readonly Dictionary<int, PlaybackInstance> _activePlaybacks = new();
    private readonly MixingSampleProvider _primaryMixer;
    private readonly MixingSampleProvider? _secondaryMixer;
    private readonly WasapiOut _primaryOutput;
    private readonly WasapiOut? _secondaryOutput;
    private readonly object _lock = new();
    private float _masterVolume = 1.0f;
    private readonly Queue<SoundClip> _clipQueue = new();
    private readonly object _queueLock = new();

    public event EventHandler<int>? ClipStarted;
    public event EventHandler<int>? ClipStopped;
    public event EventHandler? QueueChanged;

    public bool HasSecondaryOutput => _secondaryOutput != null;

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

    public AudioPlaybackService(string? primaryDeviceId = null, string? secondaryDeviceId = null)
    {
        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        // Primary output (your headphones)
        var primaryDevice = GetOutputDevice(primaryDeviceId);
        _primaryOutput = primaryDevice != null
            ? new WasapiOut(primaryDevice, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 100)
            : new WasapiOut(NAudio.CoreAudioApi.AudioClientShareMode.Shared, 100);

        _primaryMixer = new MixingSampleProvider(waveFormat) { ReadFully = true };
        _primaryOutput.Init(_primaryMixer);
        _primaryOutput.Play();

        // Secondary output (virtual cable for OBS) - optional
        if (!string.IsNullOrEmpty(secondaryDeviceId))
        {
            var secondaryDevice = GetOutputDevice(secondaryDeviceId);
            if (secondaryDevice != null)
            {
                try
                {
                    _secondaryOutput = new WasapiOut(secondaryDevice, NAudio.CoreAudioApi.AudioClientShareMode.Shared, true, 100);
                    _secondaryMixer = new MixingSampleProvider(waveFormat) { ReadFully = true };
                    _secondaryOutput.Init(_secondaryMixer);
                    _secondaryOutput.Play();
                    System.Diagnostics.Debug.WriteLine($"Secondary output initialized: {secondaryDevice.FriendlyName}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to initialize secondary output: {ex.Message}");
                    _secondaryOutput = null;
                    _secondaryMixer = null;
                }
            }
        }
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
                // Create primary audio chain
                var primaryReader = new AudioFileReader(clip.FilePath);
                var primaryProvider = CreateAudioChain(primaryReader, clip);

                // Create secondary audio chain if secondary output exists
                AudioFileReader? secondaryReader = null;
                VolumeSampleProvider? secondaryProvider = null;
                if (_secondaryMixer != null)
                {
                    secondaryReader = new AudioFileReader(clip.FilePath);
                    secondaryProvider = CreateAudioChain(secondaryReader, clip);
                }

                var playback = new PlaybackInstance(
                    clip.Id,
                    primaryReader,
                    primaryProvider,
                    clip.Volume,
                    clip.IsLooping,
                    clip.FadeOutSeconds,
                    secondaryReader,
                    secondaryProvider);
                _activePlaybacks[clip.Id] = playback;

                // Add to primary mixer (your headphones)
                _primaryMixer.AddMixerInput(primaryProvider);

                // Add to secondary mixer (virtual cable for OBS)
                if (_secondaryMixer != null && secondaryProvider != null)
                {
                    _secondaryMixer.AddMixerInput(secondaryProvider);
                }

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

    private VolumeSampleProvider CreateAudioChain(AudioFileReader reader, SoundClip clip)
    {
        ISampleProvider sampleProvider = reader;

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

        return new VolumeSampleProvider(sampleProvider)
        {
            Volume = clip.Volume * _masterVolume
        };
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

        while (true)
        {
            await Task.Delay(100);

            lock (_lock)
            {
                if (!_activePlaybacks.ContainsKey(clip.Id))
                    return; // Was stopped externally
            }

            // Check if near end for fade out
            var remaining = playback.Reader.TotalTime - playback.Reader.CurrentTime;
            if (playback.FadeOutSeconds > 0 && remaining.TotalSeconds <= playback.FadeOutSeconds && !playback.FadeOutStarted)
            {
                playback.FadeOutStarted = true;
                // Gradually reduce volume for fade out
                _ = ApplyFadeOutAsync(playback);
            }

            if (playback.Reader.Position >= playback.Reader.Length)
            {
                if (playback.IsLooping)
                {
                    // Reset to beginning for loop (both readers)
                    playback.Reader.Position = 0;
                    if (playback.SecondaryReader != null)
                    {
                        playback.SecondaryReader.Position = 0;
                    }
                    playback.FadeOutStarted = false;
                }
                else
                {
                    break;
                }
            }
        }

        StopClip(clip.Id);
    }

    private async Task ApplyFadeOutAsync(PlaybackInstance playback)
    {
        var steps = 20;
        var stepTime = (playback.FadeOutSeconds * 1000) / steps;
        var volumeStep = playback.ClipVolume / steps;

        for (int i = 0; i < steps; i++)
        {
            await Task.Delay((int)stepTime);
            var newVolume = Math.Max(0, playback.VolumeProvider.Volume - (volumeStep * _masterVolume));
            playback.VolumeProvider.Volume = newVolume;
            if (playback.SecondaryVolumeProvider != null)
            {
                playback.SecondaryVolumeProvider.Volume = newVolume;
            }
        }
    }

    public void StopClip(int clipId)
    {
        lock (_lock)
        {
            if (_activePlaybacks.TryGetValue(clipId, out var playback))
            {
                _primaryMixer.RemoveMixerInput(playback.VolumeProvider);
                if (_secondaryMixer != null && playback.SecondaryVolumeProvider != null)
                {
                    _secondaryMixer.RemoveMixerInput(playback.SecondaryVolumeProvider);
                }
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
        _primaryOutput.Stop();
        _primaryOutput.Dispose();
        _secondaryOutput?.Stop();
        _secondaryOutput?.Dispose();
    }

    private class PlaybackInstance : IDisposable
    {
        public int ClipId { get; }
        public AudioFileReader Reader { get; }
        public VolumeSampleProvider VolumeProvider { get; }
        public AudioFileReader? SecondaryReader { get; }
        public VolumeSampleProvider? SecondaryVolumeProvider { get; }
        public float ClipVolume { get; set; }
        public bool IsLooping { get; }
        public float FadeOutSeconds { get; }
        public bool FadeOutStarted { get; set; }

        public PlaybackInstance(
            int clipId,
            AudioFileReader reader,
            VolumeSampleProvider volumeProvider,
            float clipVolume,
            bool isLooping = false,
            float fadeOutSeconds = 0,
            AudioFileReader? secondaryReader = null,
            VolumeSampleProvider? secondaryVolumeProvider = null)
        {
            ClipId = clipId;
            Reader = reader;
            VolumeProvider = volumeProvider;
            SecondaryReader = secondaryReader;
            SecondaryVolumeProvider = secondaryVolumeProvider;
            ClipVolume = clipVolume;
            IsLooping = isLooping;
            FadeOutSeconds = fadeOutSeconds;
        }

        public void UpdateVolume(float masterVolume)
        {
            VolumeProvider.Volume = ClipVolume * masterVolume;
            if (SecondaryVolumeProvider != null)
            {
                SecondaryVolumeProvider.Volume = ClipVolume * masterVolume;
            }
        }

        public void Dispose()
        {
            Reader.Dispose();
            SecondaryReader?.Dispose();
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
/// This is a simple approximation that works for small pitch changes
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
        // Each semitone is a factor of 2^(1/12)
        _pitchFactor = (float)Math.Pow(2, semitones / 12.0);
        _sourceBuffer = new float[_source.WaveFormat.SampleRate * _source.WaveFormat.Channels * 2];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // For pitch up, we need more source samples
        // For pitch down, we need fewer source samples
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
