using CommunityToolkit.Mvvm.ComponentModel;

namespace TgdSoundboard.Models;

public partial class SoundClip : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private int _categoryId;

    [ObservableProperty]
    private string _hotkey = string.Empty;

    [ObservableProperty]
    private string _color = "#2196F3";

    [ObservableProperty]
    private float _volume = 1.0f;

    [ObservableProperty]
    private int _sortOrder;

    [ObservableProperty]
    private TimeSpan _duration;

    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isLooping;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private float _fadeInSeconds;

    [ObservableProperty]
    private float _fadeOutSeconds;

    [ObservableProperty]
    private float _playbackSpeed = 1.0f;

    [ObservableProperty]
    private int _pitchSemitones;
}
