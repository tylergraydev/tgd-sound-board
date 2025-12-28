using CommunityToolkit.Mvvm.ComponentModel;

namespace TgdSoundboard.Models;

public partial class AudioDevice : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private AudioDeviceType _deviceType;

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private bool _isVirtualCable;
}

public enum AudioDeviceType
{
    Output,
    Input,
    Loopback
}
