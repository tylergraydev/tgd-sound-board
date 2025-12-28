using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using TgdSoundboard.Models;

namespace TgdSoundboard.Services;

public class AudioRouterService : IDisposable
{
    private WasapiOut? _virtualCableOutput;
    private WasapiCapture? _microphoneCapture;
    private WasapiLoopbackCapture? _loopbackCapture;
    private BufferedWaveProvider? _microphoneBuffer;
    private BufferedWaveProvider? _loopbackBuffer;
    private MixingSampleProvider? _routingMixer;
    private readonly object _lock = new();

    private bool _isRouting;
    private string? _virtualCableDeviceId;

    public bool IsRoutingActive => _isRouting;
    public bool IsVirtualCableAvailable => FindVirtualCableDevice() != null;

    public event EventHandler<bool>? RoutingStatusChanged;

    public static MMDevice? FindVirtualCableDevice()
    {
        var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (device.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
                device.FriendlyName.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }
        return null;
    }

    public bool StartRouting(string? virtualCableDeviceId = null, bool passSystemAudio = false, bool passMicrophone = false,
        string? loopbackDeviceId = null, string? microphoneDeviceId = null)
    {
        lock (_lock)
        {
            if (_isRouting) StopRouting();

            try
            {
                // Find virtual cable device
                MMDevice? virtualCable;
                if (!string.IsNullOrEmpty(virtualCableDeviceId))
                {
                    var enumerator = new MMDeviceEnumerator();
                    virtualCable = enumerator.GetDevice(virtualCableDeviceId);
                }
                else
                {
                    virtualCable = FindVirtualCableDevice();
                }

                if (virtualCable == null)
                {
                    return false;
                }

                _virtualCableDeviceId = virtualCable.ID;

                // Create mixer for routing
                _routingMixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
                {
                    ReadFully = true
                };

                // Set up system audio loopback
                if (passSystemAudio)
                {
                    SetupLoopbackCapture(loopbackDeviceId);
                }

                // Set up microphone pass-through
                if (passMicrophone)
                {
                    SetupMicrophoneCapture(microphoneDeviceId);
                }

                // Start output to virtual cable
                _virtualCableOutput = new WasapiOut(virtualCable, AudioClientShareMode.Shared, true, 100);
                _virtualCableOutput.Init(_routingMixer);
                _virtualCableOutput.Play();

                _isRouting = true;
                RoutingStatusChanged?.Invoke(this, true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting routing: {ex.Message}");
                StopRouting();
                return false;
            }
        }
    }

    private void SetupLoopbackCapture(string? deviceId)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;

        if (!string.IsNullOrEmpty(deviceId))
        {
            device = enumerator.GetDevice(deviceId);
        }
        else
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }

        _loopbackCapture = new WasapiLoopbackCapture(device);

        _loopbackBuffer = new BufferedWaveProvider(_loopbackCapture.WaveFormat)
        {
            BufferLength = _loopbackCapture.WaveFormat.AverageBytesPerSecond * 2,
            DiscardOnBufferOverflow = true
        };

        _loopbackCapture.DataAvailable += (s, e) =>
        {
            _loopbackBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };

        var loopbackSampleProvider = ConvertToStereo44100(_loopbackBuffer.ToSampleProvider(), _loopbackCapture.WaveFormat);
        _routingMixer?.AddMixerInput(loopbackSampleProvider);

        _loopbackCapture.StartRecording();
    }

    private void SetupMicrophoneCapture(string? deviceId)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice device;

        if (!string.IsNullOrEmpty(deviceId))
        {
            device = enumerator.GetDevice(deviceId);
        }
        else
        {
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        }

        _microphoneCapture = new WasapiCapture(device);

        _microphoneBuffer = new BufferedWaveProvider(_microphoneCapture.WaveFormat)
        {
            BufferLength = _microphoneCapture.WaveFormat.AverageBytesPerSecond * 2,
            DiscardOnBufferOverflow = true
        };

        _microphoneCapture.DataAvailable += (s, e) =>
        {
            _microphoneBuffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };

        var micSampleProvider = ConvertToStereo44100(_microphoneBuffer.ToSampleProvider(), _microphoneCapture.WaveFormat);
        _routingMixer?.AddMixerInput(micSampleProvider);

        _microphoneCapture.StartRecording();
    }

    private ISampleProvider ConvertToStereo44100(ISampleProvider source, WaveFormat originalFormat)
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

    public void StopRouting()
    {
        lock (_lock)
        {
            _loopbackCapture?.StopRecording();
            _loopbackCapture?.Dispose();
            _loopbackCapture = null;

            _microphoneCapture?.StopRecording();
            _microphoneCapture?.Dispose();
            _microphoneCapture = null;

            _virtualCableOutput?.Stop();
            _virtualCableOutput?.Dispose();
            _virtualCableOutput = null;

            _routingMixer = null;
            _loopbackBuffer = null;
            _microphoneBuffer = null;

            _isRouting = false;
            RoutingStatusChanged?.Invoke(this, false);
        }
    }

    public void AddAudioSource(ISampleProvider source)
    {
        lock (_lock)
        {
            _routingMixer?.AddMixerInput(source);
        }
    }

    public void RemoveAudioSource(ISampleProvider source)
    {
        lock (_lock)
        {
            _routingMixer?.RemoveMixerInput(source);
        }
    }

    public void Dispose()
    {
        StopRouting();
    }
}
