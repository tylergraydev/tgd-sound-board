using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TgdSoundboard.Models;
using TgdSoundboard.Services;

namespace TgdSoundboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Category> _categories = new();

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<AudioDevice> _outputDevices = new();

    [ObservableProperty]
    private ObservableCollection<AudioDevice> _inputDevices = new();

    [ObservableProperty]
    private AudioDevice? _selectedOutputDevice;

    [ObservableProperty]
    private float _masterVolume = 1.0f;

    [ObservableProperty]
    private AppSettings _settings = new();

    [ObservableProperty]
    private bool _isClipEditorOpen;

    [ObservableProperty]
    private string? _clipEditorFilePath;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SoundClip> _favoriteClips = new();

    [ObservableProperty]
    private bool _hasFavorites;

    [ObservableProperty]
    private ObservableCollection<SoundClip> _queuedClips = new();

    [ObservableProperty]
    private int _queueCount;

    [ObservableProperty]
    private bool _hasQueuedClips;

    // Streamlabs properties
    [ObservableProperty]
    private bool _isStreamlabsConnected;

    [ObservableProperty]
    private string _streamlabsStatus = "Not connected";

    private Dictionary<int, List<SoundClip>> _allClips = new();

    public MainViewModel()
    {
        App.AudioPlayback.ClipStarted += OnClipStarted;
        App.AudioPlayback.ClipStopped += OnClipStopped;
        App.AudioPlayback.QueueChanged += OnQueueChanged;

        // Subscribe to Streamlabs events
        App.Streamlabs.ConnectionChanged += OnStreamlabsConnectionChanged;
        App.Streamlabs.ErrorOccurred += OnStreamlabsError;
        App.Streamlabs.ReplaySaved += OnReplaySaved;
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        RefreshQueue();
    }

    public void RefreshQueue()
    {
        QueuedClips.Clear();
        foreach (var clip in App.AudioPlayback.GetQueue())
        {
            QueuedClips.Add(clip);
        }
        QueueCount = QueuedClips.Count;
        HasQueuedClips = QueueCount > 0;
    }

    public async Task InitializeAsync()
    {
        // Load settings
        Settings = await App.Database.GetAppSettingsAsync();
        MasterVolume = Settings.MasterVolume;

        // Load audio devices
        RefreshAudioDevices();

        // Load categories
        var categories = await App.Database.GetCategoriesAsync();
        Categories = new ObservableCollection<Category>(categories);

        if (Categories.Count > 0)
        {
            SelectedCategory = Categories[0];
            SelectedCategory.IsSelected = true;
        }

        // Load favorites
        RefreshFavorites();
    }

    public void RefreshFavorites()
    {
        FavoriteClips.Clear();
        foreach (var category in Categories)
        {
            foreach (var clip in category.Clips.Where(c => c.IsFavorite))
            {
                FavoriteClips.Add(clip);
            }
        }
        HasFavorites = FavoriteClips.Count > 0;
    }

    private void RefreshAudioDevices()
    {
        var outputs = AudioPlaybackService.GetOutputDevices();
        OutputDevices = new ObservableCollection<AudioDevice>(outputs);

        var inputs = AudioPlaybackService.GetInputDevices();
        InputDevices = new ObservableCollection<AudioDevice>(inputs);

        // Select saved output device
        SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == Settings.OutputDeviceId)
            ?? OutputDevices.FirstOrDefault(d => d.IsDefault);
    }

    partial void OnMasterVolumeChanged(float value)
    {
        App.AudioPlayback.MasterVolume = value;
        Settings.MasterVolume = value;
        _ = App.Database.SaveAppSettingsAsync(Settings);
    }

    partial void OnSelectedCategoryChanged(Category? value)
    {
        foreach (var category in Categories)
        {
            category.IsSelected = category == value;
        }
    }

    [RelayCommand]
    private void PlayClip(SoundClip clip)
    {
        if (clip.IsPlaying)
        {
            App.AudioPlayback.StopClip(clip.Id);
        }
        else
        {
            App.AudioPlayback.PlayClip(clip);
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        App.AudioPlayback.StopAll();
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        var category = await App.Database.AddCategoryAsync("New Category");
        Categories.Add(category);
        SelectedCategory = category;
    }

    [RelayCommand]
    private async Task DeleteCategoryAsync(Category category)
    {
        if (Categories.Count <= 1) return;

        await App.Database.DeleteCategoryAsync(category.Id);
        Categories.Remove(category);

        if (SelectedCategory == category)
        {
            SelectedCategory = Categories.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task AddClipAsync(string filePath)
    {
        if (SelectedCategory == null) return;

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var storedPath = await App.ClipStorage.ImportAudioFileAsync(filePath, fileName);
        var duration = AudioPlaybackService.GetAudioDuration(storedPath);

        var clip = new SoundClip
        {
            Name = fileName,
            FilePath = storedPath,
            CategoryId = SelectedCategory.Id,
            Duration = duration
        };

        await App.Database.AddClipAsync(clip);
        SelectedCategory.Clips.Add(clip);
    }

    [RelayCommand]
    private async Task DeleteClipAsync(SoundClip clip)
    {
        App.AudioPlayback.StopClip(clip.Id);
        App.ClipStorage.DeleteClipFile(clip.FilePath);
        await App.Database.DeleteClipAsync(clip.Id);

        SelectedCategory?.Clips.Remove(clip);
    }

    [RelayCommand]
    private void OpenClipEditor(string filePath)
    {
        ClipEditorFilePath = filePath;
        IsClipEditorOpen = true;
    }

    [RelayCommand]
    private void CloseClipEditor()
    {
        IsClipEditorOpen = false;
        ClipEditorFilePath = null;
    }

    [RelayCommand]
    private async Task SaveTrimmedClipAsync((string FilePath, string Name, TimeSpan Start, TimeSpan End) args)
    {
        if (SelectedCategory == null) return;

        var storedPath = await App.ClipStorage.SaveTrimmedClipAsync(args.FilePath, args.Name, args.Start, args.End);
        var duration = args.End - args.Start;

        var clip = new SoundClip
        {
            Name = args.Name,
            FilePath = storedPath,
            CategoryId = SelectedCategory.Id,
            Duration = duration
        };

        await App.Database.AddClipAsync(clip);
        SelectedCategory.Clips.Add(clip);

        CloseClipEditor();
    }

    private void OnClipStarted(object? sender, int clipId)
    {
        var clip = FindClipById(clipId);
        if (clip != null)
        {
            clip.IsPlaying = true;
        }
    }

    private void OnClipStopped(object? sender, int clipId)
    {
        var clip = FindClipById(clipId);
        if (clip != null)
        {
            clip.IsPlaying = false;
        }
    }

    private SoundClip? FindClipById(int clipId)
    {
        foreach (var category in Categories)
        {
            var clip = category.Clips.FirstOrDefault(c => c.Id == clipId);
            if (clip != null) return clip;
        }
        return null;
    }

    public void FilterClips(string searchText)
    {
        SearchFilter = searchText;

        // Store all clips on first filter
        if (_allClips.Count == 0)
        {
            foreach (var category in Categories)
            {
                _allClips[category.Id] = category.Clips.ToList();
            }
        }

        foreach (var category in Categories)
        {
            if (!_allClips.TryGetValue(category.Id, out var allCategoryClips))
                continue;

            category.Clips.Clear();

            if (string.IsNullOrWhiteSpace(searchText))
            {
                // Show all clips
                foreach (var clip in allCategoryClips.OrderBy(c => c.SortOrder))
                {
                    category.Clips.Add(clip);
                }
            }
            else
            {
                // Show filtered clips
                var filtered = allCategoryClips
                    .Where(c => c.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.SortOrder);

                foreach (var clip in filtered)
                {
                    category.Clips.Add(clip);
                }
            }
        }
    }

    public void ClearFilter()
    {
        FilterClips(string.Empty);
        _allClips.Clear();
    }

    // Streamlabs methods
    private void OnStreamlabsConnectionChanged(object? sender, bool isConnected)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsStreamlabsConnected = isConnected;
            StreamlabsStatus = isConnected ? "Connected" : "Disconnected";
        });
    }

    private void OnStreamlabsError(object? sender, string error)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StreamlabsStatus = error;
        });
    }

    private void OnReplaySaved(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            StreamlabsStatus = "Replay saved!";
        });
    }

    [RelayCommand]
    private async Task ConnectStreamlabsAsync()
    {
        if (string.IsNullOrEmpty(Settings.StreamlabsToken))
        {
            StreamlabsStatus = "No token configured";
            return;
        }

        StreamlabsStatus = "Connecting...";
        App.Streamlabs.SetToken(Settings.StreamlabsToken);
        var success = await App.Streamlabs.ConnectAsync();

        if (!success)
        {
            StreamlabsStatus = App.Streamlabs.LastError ?? "Connection failed";
        }
    }

    [RelayCommand]
    private async Task DisconnectStreamlabsAsync()
    {
        await App.Streamlabs.DisconnectAsync();
        StreamlabsStatus = "Disconnected";
    }

    [RelayCommand]
    private async Task SaveReplayAsync()
    {
        if (!IsStreamlabsConnected)
        {
            StreamlabsStatus = "Not connected";
            return;
        }

        StreamlabsStatus = "Saving replay...";
        var success = await App.Streamlabs.SaveReplayAsync();

        if (!success)
        {
            StreamlabsStatus = App.Streamlabs.LastError ?? "Failed to save replay";
        }
    }

    public async Task SaveStreamlabsSettingsAsync(string token, bool autoConnect, string replayScene)
    {
        Settings.StreamlabsToken = token;
        Settings.StreamlabsAutoConnect = autoConnect;
        Settings.StreamlabsReplayScene = replayScene;
        await App.Database.SaveAppSettingsAsync(Settings);

        // Update the service token
        App.Streamlabs.SetToken(token);
    }
}
