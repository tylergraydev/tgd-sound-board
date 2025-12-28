using CommunityToolkit.Mvvm.ComponentModel;

namespace TgdSoundboard.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty]
    private string _outputDeviceId = string.Empty;

    [ObservableProperty]
    private float _masterVolume = 1.0f;

    [ObservableProperty]
    private int _gridColumns = 6;

    [ObservableProperty]
    private string _clipsDirectory = string.Empty;

    [ObservableProperty]
    private string _theme = "Neon";

    [ObservableProperty]
    private string _virtualCableDeviceId = string.Empty;

    [ObservableProperty]
    private string _inputDeviceId = string.Empty;

    [ObservableProperty]
    private string _loopbackDeviceId = string.Empty;

    [ObservableProperty]
    private bool _passSystemAudio;

    [ObservableProperty]
    private bool _passMicrophone;

    // Streamlabs settings
    [ObservableProperty]
    private string _streamlabsToken = string.Empty;

    [ObservableProperty]
    private bool _streamlabsAutoConnect;

    [ObservableProperty]
    private string _streamlabsReplayScene = string.Empty;
}
